namespace ClaudeMeter.Core;

/// <summary>
/// Everything account-related the tray shows and does. Runs on the UI thread. Multi-step writes
/// (auto-save, switch, token refresh) go through one <see cref="OperationGate"/>; usage fetches do not.
/// </summary>
internal sealed class AccountsRuntime
{
    /// <summary>Monitor key while Claude Code has no identity (logged out): its usage shows "No credentials".</summary>
    private static readonly AccountKey NoAccount = new("", "");

    private readonly ClaudePaths _paths;
    private readonly AccountStore _store;
    private readonly AppSettings _settings;
    private readonly IUsageClient _usageClient;
    private readonly TimeProvider _clock;
    private readonly OperationGate _gate = new();
    private readonly AutoSaver _autoSaver;
    private readonly AccountSwitcher _switcher;
    private readonly TokenRefresher _refresher;
    private readonly Dictionary<AccountKey, UsageService> _usage = [];

    private OAuthAccount? _active;
    private CredentialsInfo? _activeCredentials;
    private string? _captureWarning;
    private bool _captureRejected;
    private AccountKey? _switching;
    private SwitchStage? _switchStage;
    private AccountKey? _switchedTo;

    public AccountsRuntime(ClaudePaths paths, AccountStore store, AppSettings settings, IUsageClient usage, IProfileClient profiles, IOAuthTokenClient tokens, ClaudeLocks locks, TimeProvider clock)
    {
        _paths = paths;
        _store = store;
        _settings = settings;
        _usageClient = usage;
        _clock = clock;
        _autoSaver = new AutoSaver(paths, locks, store, profiles, clock);
        // While captures are rejected (Claude Code's files disagree) nobody can say which account Claude Code
        // holds, so no inactive account is refreshed: one of them might be the one it is really using.
        _refresher = new TokenRefresher(
            store, tokens, () => _captureRejected || _autoSaver.RequireProfile ? null : ClaudeHolder.Read(paths), () => settings.KeepInactiveLive, clock);
        _refresher.Disabled += (_, _) => Notify?.Invoke(this, Notifications.RefreshStopped);
        _switcher = new AccountSwitcher(paths, locks, store, profiles, _refresher, clock);
    }

    public event EventHandler? Changed;

    public event EventHandler<Notification>? Notify;

    public bool KeepInactiveLive => _settings.KeepInactiveLive;

    private AccountKey ActiveKey => _active?.Key ?? NoAccount;

    public UsageState ActiveUsage => Usage(ActiveKey).State;

    public TimeSpan TimeUntilNextDue
    {
        get
        {
            var next = PollableKeys().Select(k => Usage(k).TimeUntilNextPoll).DefaultIfEmpty(RefreshPolicy.PollInterval).Min();
            return next < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : next;
        }
    }

    /// <summary>Saved accounts in AddedAt order, then the active login if it is not saved yet.</summary>
    public IReadOnlyList<AccountView> Accounts
    {
        get
        {
            var now = _clock.GetUtcNow();
            var views = _store.Accounts.Select(a => View(a, now)).ToList();
            if (_active is { } active && _store.Find(active.Key) is null)
            {
                views.Add(new AccountView(
                    active.Key, active.Email, _activeCredentials?.SubscriptionType, IsActive: true, IsSaved: false,
                    Usage(active.Key).State,
                    LoginExpiry.Describe(_activeCredentials?.RefreshTokenExpiresAt, LoginState.Valid, null, isActive: true, now),
                    Switching: null, SwitchedHere: false, _captureWarning, _activeCredentials?.RefreshTokenExpiresAt));
            }

            return views;
        }
    }

    public async Task StartAsync(StoreLoadResult loaded)
    {
        if (loaded == StoreLoadResult.SetAside)
            Notify?.Invoke(this, Notifications.StoreSetAside);
        foreach (var account in _store.Accounts)
        {
            if (account.LastKnownUsage is { } snapshot && account.LastKnownAt is { } at)
                Usage(account.Key).Seed(snapshot, at);
        }

        await PollAsync(manual: false, CancellationToken.None);
    }

    /// <summary>Polls every account that is due (manual: every account, each subject to its own floor).</summary>
    public async Task PollAsync(bool manual, CancellationToken cancellationToken)
    {
        ReadActive();
        if (manual || Usage(ActiveKey).TimeUntilNextPoll == TimeSpan.Zero)
            await AutoSaveAsync(cancellationToken);

        foreach (var key in PollableKeys().ToList())
        {
            var service = Usage(key);
            if (!manual && service.TimeUntilNextPoll > TimeSpan.Zero)
                continue;

            var before = service.State;
            var after = await service.RefreshAsync(manual, cancellationToken);
            if (after.Status == UsageStatus.Ok && !ReferenceEquals(after.Snapshot, before.Snapshot) && _store.Find(key) is { } saved)
                _store.Upsert(saved with { LastKnownUsage = after.Snapshot, LastKnownAt = after.LastUpdated });
        }

        WarnExpiringLogins();
        if (_store.HasUnsavedChanges)
            _store.TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task OnClaudeFilesChangedAsync()
    {
        ReadActive();
        await AutoSaveAsync(CancellationToken.None);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SwitchAsync(AccountKey key)
    {
        if (_switching is not null)
            return;

        _switching = key;
        _switchStage = SwitchStage.WaitingForLock;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            SwitchResult result;
            var progress = new InlineProgress(stage =>
            {
                _switchStage = stage;
                Changed?.Invoke(this, EventArgs.Empty);
            });
            using (await _gate.EnterAsync(CancellationToken.None))
                result = await _switcher.SwitchAsync(key, progress, CancellationToken.None);

            switch (result)
            {
                case SwitchResult.Switched switched:
                    _switchedTo = key;
                    Notify?.Invoke(this, Notifications.Switched(switched.Email));
                    break;
                case SwitchResult.Unconfirmed unconfirmed:
                    _autoSaver.RequireProfile = true;
                    Notify?.Invoke(this, Notifications.SwitchUnconfirmed(unconfirmed.Email));
                    break;
                case SwitchResult.Failed failed:
                    Notify?.Invoke(this, Notifications.SwitchFailed(failed.Message));
                    break;
            }
        }
        finally
        {
            _switching = null;
            _switchStage = null;
        }

        await PollAsync(manual: true, CancellationToken.None);
    }

    /// <summary>Deletes the saved snapshot only; Claude Code's files are not touched.</summary>
    public async Task RemoveAsync(AccountKey key)
    {
        using (await _gate.EnterAsync(CancellationToken.None))
        {
            if (_store.Remove(key))
                _store.TrySave();
        }

        if (key != ActiveKey)
            _usage.Remove(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetKeepInactiveLive(bool on)
    {
        _settings.SetKeepInactiveLive(on);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task AutoSaveAsync(CancellationToken cancellationToken)
    {
        AutoSaveResult result;
        using (await _gate.EnterAsync(cancellationToken))
            result = await _autoSaver.RunAsync(cancellationToken);

        _captureWarning = result switch
        {
            AutoSaveResult.Deferred => "Not saved yet: waiting for network to verify",
            AutoSaveResult.Rejected => "Login files are changing; will retry",
            AutoSaveResult.Busy => _captureWarning,
            _ => null,
        };
        if (result is not AutoSaveResult.Busy)
            _captureRejected = result is AutoSaveResult.Rejected;
        if (result is AutoSaveResult.Inserted inserted)
            Notify?.Invoke(this, Notifications.Added(inserted.Email));
        ReadActive();
    }

    /// <summary>Who Claude Code says is signed in; keeps the last answer if the files are mid-write.</summary>
    private void ReadActive()
    {
        try
        {
            var files = LiveFiles.Read(_paths);
            _active = files.GlobalConfig is { } config ? OAuthAccount.Parse(config) : null;
            _activeCredentials = files.Credentials is { } credentials ? CredentialsInfo.Parse(credentials) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
        }

        if (_switchedTo is { } switched && switched != ActiveKey)
            _switchedTo = null;
    }

    private IEnumerable<AccountKey> PollableKeys()
    {
        var now = _clock.GetUtcNow();
        yield return ActiveKey;
        foreach (var account in _store.Accounts)
        {
            if (account.Key != ActiveKey && !LoginExpiry.IsExpired(account, now))
                yield return account.Key;
        }
    }

    private UsageService Usage(AccountKey key)
    {
        if (!_usage.TryGetValue(key, out var service))
        {
            var source = new AccountTokenSource(key, () => ActiveKey, () => LiveFiles.ReadAccessToken(_paths), _store, _refresher, _gate, _clock);
            _usage[key] = service = new UsageService(_usageClient, source, new RefreshPolicy(_clock));
        }

        return service;
    }

    private AccountView View(StoredAccount account, DateTimeOffset now)
    {
        var isActive = account.Key == ActiveKey;
        var loginExpiresAt = isActive ? _activeCredentials?.RefreshTokenExpiresAt : account.Credentials?.RefreshTokenExpiresAt;
        return new AccountView(
            account.Key, account.Email, account.Plan, isActive, IsSaved: true, Usage(account.Key).State,
            LoginExpiry.Describe(loginExpiresAt, isActive ? LoginState.Valid : account.LoginState, account.LastActiveAt, isActive, now),
            _switching == account.Key ? _switchStage : null,
            SwitchedHere: _switchedTo == account.Key,
            isActive ? _captureWarning : null,
            loginExpiresAt);
    }

    private void WarnExpiringLogins()
    {
        var now = _clock.GetUtcNow();
        foreach (var account in _store.Accounts.ToList())
        {
            if (LoginExpiry.WarningDue(account, now) is not { } inDays)
                continue;
            _store.Upsert(account with { ExpiryWarnedFor = account.Credentials!.RefreshTokenExpiresAt!.Value.ToUnixTimeMilliseconds() });
            Notify?.Invoke(this, Notifications.LoginExpiring(account.Email, inDays));
        }
    }

    /// <summary>Reports synchronously on the caller's thread (Progress&lt;T&gt; would post asynchronously).</summary>
    private sealed class InlineProgress(Action<SwitchStage> report) : IProgress<SwitchStage>
    {
        public void Report(SwitchStage value) => report(value);
    }
}
