namespace ClaudeMeter.Core;

/// <summary>
/// Reads the token, calls the endpoint when <see cref="RefreshPolicy"/> allows it, and folds the
/// result into <see cref="State"/>. All state transitions happen here.
/// </summary>
internal sealed class UsageService(IUsageClient client, Func<string?> readAccessToken, RefreshPolicy policy)
{
    private bool _inFlight;

    public UsageState State { get; private set; } = UsageState.Initial;

    public TimeSpan TimeUntilNextPoll => policy.TimeUntilNextPoll;

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
        string? token;
        try
        {
            // Re-read on every poll so a token refreshed by Claude Code is picked up.
            token = readAccessToken();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(UsageStatus.Error, "credentials file unreadable");
        }

        if (token is null)
            return Fail(UsageStatus.NoCredentials, "no credentials");

        switch (await client.FetchAsync(token, cancellationToken))
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
}
