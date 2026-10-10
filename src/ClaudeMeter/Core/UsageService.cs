namespace ClaudeMeter.Core;

internal abstract record TokenLookup
{
    private TokenLookup() { }

    public sealed record Found(string AccessToken) : TokenLookup;
    public sealed record NoCredentials : TokenLookup;
    public sealed record NotLive : TokenLookup;
}

internal interface IAccessTokenSource
{
    /// <exception cref="IOException">A credentials file could not be read; treated as transient.</exception>
    Task<TokenLookup> GetAsync(CancellationToken cancellationToken);

    /// <summary>After a 401: a new token to retry once with, or null.</summary>
    Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken);
}

/// <summary>
/// Gets a token, calls the endpoint when <see cref="RefreshPolicy"/> allows it, and folds the
/// result into <see cref="State"/>. All state transitions happen here.
/// </summary>
internal sealed class UsageService(IUsageClient client, IAccessTokenSource tokens, RefreshPolicy policy)
{
    private bool _inFlight;

    public UsageService(IUsageClient client, Func<string?> readAccessToken, RefreshPolicy policy)
        : this(client, new DelegateTokenSource(readAccessToken), policy)
    {
    }

    public UsageState State { get; private set; } = UsageState.Initial;

    public TimeSpan TimeUntilNextPoll => policy.TimeUntilNextPoll;

    /// <summary>Shows saved usage until the first fetch completes.</summary>
    public void Seed(UsageSnapshot snapshot, DateTimeOffset fetchedAt)
    {
        if (State.Snapshot is null)
            State = State with { Snapshot = snapshot, LastUpdated = fetchedAt };
    }

    /// <summary>Fetches fresh data if allowed; otherwise returns the cached state.</summary>
    public async Task<UsageState> RefreshAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (_inFlight || !policy.TryBeginRequest(manual))
            return State;

        _inFlight = true;
        try
        {
            State = await FetchAsync(cancellationToken);
        }
        finally
        {
            _inFlight = false;
        }

        return State;
    }

    private async Task<UsageState> FetchAsync(CancellationToken cancellationToken)
    {
        TokenLookup lookup;
        try
        {
            // Re-read on every poll so a token refreshed by Claude Code is picked up.
            lookup = await tokens.GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(UsageStatus.Error, "credentials file unreadable");
        }

        string token;
        switch (lookup)
        {
            case TokenLookup.Found found:
                token = found.AccessToken;
                break;
            case TokenLookup.NotLive:
                return State with { Status = UsageStatus.NotLive, Error = null };
            default:
                return Fail(UsageStatus.NoCredentials, "no credentials");
        }

        var result = await client.FetchAsync(token, cancellationToken);
        if (result is FetchResult.Unauthorized && await tokens.RenewAsync(token, cancellationToken) is { } renewed)
            result = await client.FetchAsync(renewed, cancellationToken);

        switch (result)
        {
            case FetchResult.Success success:
                policy.OnSuccess();
                return new UsageState(UsageStatus.Ok, success.Snapshot, policy.Now, null);
            case FetchResult.Unauthorized:
                return Fail(UsageStatus.TokenExpired, "token expired");
            case FetchResult.RateLimited rateLimited:
                policy.OnRateLimited(rateLimited.RetryAfter);
                return Fail(UsageStatus.Error, "rate limited");
            case FetchResult.Failed failed:
                return Fail(UsageStatus.Error, failed.Reason);
            default:
                throw new InvalidOperationException("Unknown fetch result.");
        }
    }

    private UsageState Fail(UsageStatus status, string error) => State with { Status = status, Error = error };

    private sealed class DelegateTokenSource(Func<string?> read) : IAccessTokenSource
    {
        public Task<TokenLookup> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<TokenLookup>(read() is { } token ? new TokenLookup.Found(token) : new TokenLookup.NoCredentials());

        public Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
