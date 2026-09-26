using System.Net.Http.Headers;

namespace ClaudeMeter.Core;

/// <summary>
/// Decides when a request may be sent: scheduled polls every 10 minutes, manual refreshes
/// no sooner than 5 minutes after the previous request, and 429 backoff.
/// </summary>
internal sealed class RefreshPolicy(TimeProvider clock)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ManualFloor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackoffBase = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromMinutes(1);

    private DateTimeOffset? _lastRequest;
    private DateTimeOffset _nextPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private int _consecutiveRateLimits;

    public DateTimeOffset Now => clock.GetUtcNow();

    public TimeSpan TimeUntilNextPoll => _nextPoll > Now ? _nextPoll - Now : TimeSpan.Zero;

    /// <summary>Returns true and records the request if one may be sent now.</summary>
    public bool TryBeginRequest(bool manual)
    {
        var now = Now;
        var allowed = manual
            ? now >= _blockedUntil && (_lastRequest is null || now - _lastRequest >= ManualFloor)
            : now >= _nextPoll;

        if (allowed)
        {
            _lastRequest = now;
            _nextPoll = now + PollInterval;
        }

        return allowed;
    }

    public void OnSuccess() => _consecutiveRateLimits = 0;

    public void OnRateLimited(RetryConditionHeaderValue? retryAfter)
    {
        _consecutiveRateLimits++;
        var now = Now;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date - now) ?? Backoff(_consecutiveRateLimits);
        if (delay < MinRetryDelay)
            delay = MinRetryDelay;

        _blockedUntil = _nextPoll = now + delay;
    }

    /// <summary>10 min × 2^n, capped at 30 minutes (n = consecutive 429s without Retry-After).</summary>
    public static TimeSpan Backoff(int consecutiveRateLimits) =>
        TimeSpan.FromTicks(Math.Min(BackoffCap.Ticks, BackoffBase.Ticks << Math.Clamp(consecutiveRateLimits, 0, 8)));
}
