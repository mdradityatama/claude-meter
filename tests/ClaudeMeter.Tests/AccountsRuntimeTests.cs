using System.Net.Http.Headers;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AccountsRuntimeTests : IDisposable
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly AccountKey A = new("acc-a", "org-a");
    private static readonly AccountKey T = new("acc-t", "org-t");
    private static readonly UsageSnapshot Snapshot = new(new UsageWindow(42, Start.AddHours(2)), new UsageWindow(18, Start.AddDays(3)));

    private readonly ClaudeHome _home = new();
    private readonly FakeUsageClient _usage = new();
    private readonly FakeProfileClient _profiles = new() { Result = new ProfileResult.Found("acc-a", "org-a") };
    private readonly FakeOAuthTokenClient _tokens = new();
    private readonly Dictionary<string, FetchResult> _responses = [];
    private readonly List<Notification> _toasts = [];
    private AppSettings _settings;

    public AccountsRuntimeTests()
    {
        _settings = AppSettings.Load(_home.Paths.Settings);
        _usage.Responder = token => _responses.TryGetValue(token, out var result) ? result : new FetchResult.Success(Snapshot);
        _home.WriteLive(CredentialsA, ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com"));
    }

    public void Dispose() => _home.Dispose();

    private static byte[] CredentialsA => ClaudeJson.Credentials("a-access", "a-refresh", Start.AddHours(8), "org-a");

    private void SaveA() => _home.Save("acc-a", "org-a", "a@example.com", CredentialsA, Start.AddDays(-2));

    private StoredAccount SaveT(DateTimeOffset? expiresAt = null, DateTimeOffset? loginExpiresAt = null) =>
        _home.Save("acc-t", "org-t", "t@example.com",
            ClaudeJson.Credentials("t-access-1", "t-refresh-1", expiresAt ?? Start.AddHours(6), "org-t", loginExpiresAt), Start.AddDays(-1));

    private AccountsRuntime Runtime(AccountStore? store = null)
    {
        var runtime = new AccountsRuntime(_home.Paths, store ?? _home.Store, _settings, _usage, _profiles, _tokens, _home.ClaudeLocks, _home.Clock);
        runtime.Notify += (_, toast) => _toasts.Add(toast);
        return runtime;
    }

    private static AccountView ViewOf(AccountsRuntime runtime, AccountKey key) => runtime.Accounts.Single(a => a.Key == key);

    [Fact]
    public async Task First_poll_fetches_every_account_with_its_own_token()
    {
        SaveA();
        SaveT();

        await Runtime().StartAsync(StoreLoadResult.Loaded);

        Assert.Equal(["a-access", "t-access-1"], _usage.TokensSeen.Order());
    }

    [Fact]
    public async Task Each_account_keeps_its_own_schedule()
    {
        SaveA();
        SaveT();
        _responses["t-access-1"] = new FetchResult.RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(45)));
        var runtime = Runtime();
        await runtime.StartAsync(StoreLoadResult.Loaded);

        _home.Clock.Advance(TimeSpan.FromMinutes(10));
        _usage.TokensSeen.Clear();
        await runtime.PollAsync(manual: false, default);

        Assert.Equal(["a-access"], _usage.TokensSeen);
        Assert.Equal(TimeSpan.FromMinutes(10), runtime.TimeUntilNextDue);
    }

    [Fact]
    public async Task Manual_refresh_respects_each_accounts_floor()
    {
        SaveA();
        SaveT();
        var runtime = Runtime();
        await runtime.StartAsync(StoreLoadResult.Loaded);
        _usage.TokensSeen.Clear();

        _home.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await runtime.PollAsync(manual: true, default);
        Assert.Empty(_usage.TokensSeen);

        _home.Clock.Advance(TimeSpan.FromSeconds(1));
        await runtime.PollAsync(manual: true, default);
        Assert.Equal(2, _usage.TokensSeen.Count);
    }

    [Fact]
    public async Task Inactive_account_without_refresh_stops_after_expires_at()
    {
        _settings.SetKeepInactiveLive(false);
        SaveA();
        var t = SaveT(expiresAt: Start.AddSeconds(-1));
        _home.Store.Upsert(t with { LastKnownUsage = Snapshot, LastKnownAt = Start.AddHours(-2) });
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Loaded);

        var view = ViewOf(runtime, T);
        Assert.Equal(UsageStatus.NotLive, view.Usage.Status);
        Assert.Equal(Snapshot, view.Usage.Snapshot);
        Assert.DoesNotContain("t-access-1", _usage.TokensSeen);
    }

    [Fact]
    public async Task Inactive_401_without_refresh_stops_polling()
    {
        _settings.SetKeepInactiveLive(false);
        SaveA();
        SaveT();
        _responses["t-access-1"] = new FetchResult.Unauthorized();
        var runtime = Runtime();
        await runtime.StartAsync(StoreLoadResult.Loaded);

        _home.Clock.Advance(TimeSpan.FromMinutes(10));
        await runtime.PollAsync(manual: false, default);

        Assert.Single(_usage.TokensSeen, "t-access-1");
        Assert.Equal(UsageStatus.NotLive, ViewOf(runtime, T).Usage.Status);
    }

    [Fact]
    public async Task Inactive_401_with_refresh_retries_once_with_the_new_token()
    {
        SaveA();
        SaveT();
        _responses["t-access-1"] = new FetchResult.Unauthorized();
        _tokens.Results.Enqueue(new TokenResult.Success("t-access-2", TimeSpan.FromHours(8), "t-refresh-2", null));
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Loaded);

        Assert.Equal(["t-refresh-1"], _tokens.RefreshTokensSeen);
        Assert.Contains("t-access-2", _usage.TokensSeen);
        Assert.Equal(UsageStatus.Ok, ViewOf(runtime, T).Usage.Status);
    }

    [Fact]
    public async Task Active_account_is_never_refreshed()
    {
        SaveA();
        _responses["a-access"] = new FetchResult.Unauthorized();
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Loaded);

        Assert.Empty(_tokens.RefreshTokensSeen);
        Assert.Equal(UsageStatus.TokenExpired, ViewOf(runtime, A).Usage.Status);
        Assert.Equal(UsageStatus.TokenExpired, runtime.ActiveUsage.Status);
    }

    [Fact]
    public async Task New_login_is_added_with_a_toast()
    {
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Empty);

        Assert.Equal([Notifications.Added("a@example.com")], _toasts);
        var view = Assert.Single(runtime.Accounts);
        Assert.True(view.IsSaved);
        Assert.True(view.IsActive);
    }

    [Fact]
    public async Task Unverified_new_login_shows_not_saved_yet()
    {
        _profiles.Result = new ProfileResult.Unavailable("offline");
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Empty);

        var view = Assert.Single(runtime.Accounts);
        Assert.False(view.IsSaved);
        Assert.Equal("Not saved yet: waiting for network to verify", view.CaptureWarning);
        Assert.Empty(_toasts);
    }

    [Fact]
    public async Task Last_known_usage_is_persisted()
    {
        SaveA();
        SaveT();

        await Runtime().StartAsync(StoreLoadResult.Loaded);

        var reloaded = new AccountStore(_home.Paths.Store, new FakeProtector());
        reloaded.Load();
        Assert.Equal(Snapshot, reloaded.Find(T)!.LastKnownUsage);
        Assert.Equal(Start, reloaded.Find(T)!.LastKnownAt);
    }

    [Fact]
    public async Task Expiry_toast_once_per_value()
    {
        SaveA();
        var t = SaveT(loginExpiresAt: Start.AddDays(1).AddHours(1));
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Loaded);
        _home.Clock.Advance(TimeSpan.FromMinutes(10));
        await runtime.PollAsync(manual: false, default);

        Assert.Equal([Notifications.LoginExpiring("t@example.com", "in 1 day")], _toasts);
        var reloaded = new AccountStore(_home.Paths.Store, new FakeProtector());
        reloaded.Load();
        Assert.Equal(t.Credentials!.RefreshTokenExpiresAt!.Value.ToUnixTimeMilliseconds(), reloaded.Find(T)!.ExpiryWarnedFor);
    }

    [Fact]
    public async Task Switch_toasts_and_moves_the_active_marker()
    {
        SaveA();
        SaveT();
        var runtime = Runtime();
        await runtime.StartAsync(StoreLoadResult.Loaded);

        await runtime.SwitchAsync(T);

        Assert.Equal([Notifications.Switched("t@example.com")], _toasts);
        Assert.Equal([A, T], runtime.Accounts.Select(a => a.Key));
        Assert.True(ViewOf(runtime, T).IsActive);
        Assert.False(ViewOf(runtime, A).IsActive);
        Assert.Null(ViewOf(runtime, T).Switching);
    }

    [Fact]
    public async Task Failed_switch_toasts_the_reason()
    {
        SaveA();
        SaveT();
        var runtime = Runtime();
        await runtime.StartAsync(StoreLoadResult.Loaded);
        _home.Locks.AddForeign(_home.Paths.OAuthRefreshLock, _home.Clock.Now);

        await runtime.SwitchAsync(T);

        Assert.Equal([Notifications.SwitchFailed(AccountSwitcher.Busy)], _toasts);
        Assert.True(ViewOf(runtime, A).IsActive);
    }

    [Fact]
    public async Task Remove_deletes_only_the_snapshot()
    {
        SaveA();
        SaveT();
        var runtime = Runtime();
        await runtime.StartAsync(StoreLoadResult.Loaded);
        var credentials = _home.LiveCredentials;
        var config = _home.LiveConfig;

        await runtime.RemoveAsync(T);

        Assert.Null(_home.Store.Find(T));
        Assert.DoesNotContain(runtime.Accounts, a => a.Key == T);
        Assert.Equal(credentials, _home.LiveCredentials);
        Assert.Equal(config, _home.LiveConfig);
    }

    [Fact]
    public async Task Invalid_client_toasts_once()
    {
        SaveA();
        SaveT(expiresAt: Start.AddMinutes(2));
        _tokens.Results.Enqueue(new TokenResult.InvalidClient());
        var runtime = Runtime();

        await runtime.StartAsync(StoreLoadResult.Loaded);
        _home.Clock.Advance(TimeSpan.FromMinutes(10));
        await runtime.PollAsync(manual: false, default);

        Assert.Equal([Notifications.RefreshStopped], _toasts);
        Assert.Single(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Unreadable_store_toasts_on_start()
    {
        SaveA();

        await Runtime().StartAsync(StoreLoadResult.SetAside);

        Assert.Contains(Notifications.StoreSetAside, _toasts);
    }
}
