namespace ClaudeMeter.Core;

/// <summary>Serializes everything that writes the store or Claude Code's files across awaits.</summary>
internal sealed class OperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}

/// <summary>
/// Token for one account's usage polls: Claude Code's live token while the account is active (never
/// refreshed by the app), otherwise the saved token, refreshed when "Keep inactive accounts live" allows.
/// </summary>
internal sealed class AccountTokenSource(
    AccountKey key,
    Func<AccountKey?> activeKey,
    Func<string?> readLiveToken,
    AccountStore store,
    TokenRefresher refresher,
    OperationGate gate,
    TimeProvider clock) : IAccessTokenSource
{
    private string? _rejected;

    public async Task<TokenLookup> GetAsync(CancellationToken cancellationToken)
    {
        if (activeKey() == key)
            return readLiveToken() is { } live ? new TokenLookup.Found(live) : new TokenLookup.NoCredentials();

        if (refresher.CanRefresh && store.Find(key) is { } stale && refresher.NeedsRefresh(stale))
        {
            using (await gate.EnterAsync(cancellationToken))
                await refresher.EnsureFreshAsync(key, cancellationToken);
        }

        if (store.Find(key) is not { LoginState: LoginState.Valid } account || account.Credentials is not { } credentials)
            return new TokenLookup.NotLive();

        var usable = credentials.ExpiresAt > clock.GetUtcNow() && credentials.AccessToken != _rejected;
        return usable ? new TokenLookup.Found(credentials.AccessToken) : new TokenLookup.NotLive();
    }

    public async Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        if (activeKey() == key)
            return null;   // Claude Code owns this token

        _rejected = rejectedToken;
        if (!refresher.CanRefresh)
            return null;

        RefreshOutcome outcome;
        using (await gate.EnterAsync(cancellationToken))
            outcome = await refresher.ForceRefreshAsync(key, cancellationToken);
        return outcome == RefreshOutcome.Refreshed ? store.Find(key)?.Credentials?.AccessToken : null;
    }
}
