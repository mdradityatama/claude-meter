using System.Net.Http.Headers;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class UsageServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly UsageSnapshot Snapshot = new(new UsageWindow(42, Start.AddHours(2)), new UsageWindow(18, Start.AddDays(3)));

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeUsageClient _client = new();
    private string? _token = "tok";
    private readonly UsageService _service;

    public UsageServiceTests()
    {
        _service = new UsageService(_client, () => _token, new RefreshPolicy(_clock));
    }

    [Fact]
    public void Initial_state_is_loading_and_first_poll_is_due_now()
    {
        Assert.Equal(UsageStatus.Loading, _service.State.Status);
        Assert.Equal(TimeSpan.Zero, _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Success_updates_state_and_schedules_next_poll_in_10_minutes()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));

        var state = await _service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.Ok, state.Status);
        Assert.False(state.IsStale);
        Assert.Same(Snapshot, state.Snapshot);
        Assert.Equal(Start, state.LastUpdated);
        Assert.Equal(TimeSpan.FromMinutes(10), _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Manual_refresh_within_5_minutes_returns_cached_without_request()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        await _service.RefreshAsync(manual: false);

        _clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        var state = await _service.RefreshAsync(manual: true);

        Assert.Single(_client.TokensSeen);
        Assert.Same(Snapshot, state.Snapshot);
    }

    [Fact]
    public async Task Manual_refresh_after_5_minutes_sends_request_and_reschedules_poll()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        await _service.RefreshAsync(manual: false);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await _service.RefreshAsync(manual: true);

        Assert.Equal(2, _client.TokensSeen.Count);
        Assert.Equal(TimeSpan.FromMinutes(10), _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Scheduled_poll_before_it_is_due_does_not_send_request()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        await _service.RefreshAsync(manual: false);

        _clock.Advance(TimeSpan.FromMinutes(9));
        await _service.RefreshAsync(manual: false);

        Assert.Single(_client.TokensSeen);
    }

    [Fact]
    public async Task Rate_limit_honors_retry_after_seconds_and_blocks_manual_refresh()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        _client.Results.Enqueue(new FetchResult.RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(45))));
        await _service.RefreshAsync(manual: false);
        _clock.Advance(TimeSpan.FromMinutes(10));

        var state = await _service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.Error, state.Status);
        Assert.True(state.IsStale);
        Assert.Same(Snapshot, state.Snapshot);
        Assert.Equal(Start, state.LastUpdated);
        Assert.Equal(TimeSpan.FromMinutes(45), _service.TimeUntilNextPoll);

        _clock.Advance(TimeSpan.FromMinutes(20));
        await _service.RefreshAsync(manual: true);
        Assert.Equal(2, _client.TokensSeen.Count);
    }

    [Fact]
    public async Task Rate_limit_honors_retry_after_date()
    {
        _client.Results.Enqueue(new FetchResult.RateLimited(new RetryConditionHeaderValue(Start.AddMinutes(7))));

        await _service.RefreshAsync(manual: false);

        Assert.Equal(TimeSpan.FromMinutes(7), _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Retry_after_in_the_past_waits_at_least_one_minute()
    {
        _client.Results.Enqueue(new FetchResult.RateLimited(new RetryConditionHeaderValue(Start.AddMinutes(-5))));

        await _service.RefreshAsync(manual: false);

        Assert.Equal(TimeSpan.FromMinutes(1), _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Rate_limit_without_retry_after_backs_off_exponentially_and_resets_on_success()
    {
        FetchResult[] results =
        [
            new FetchResult.RateLimited(null),
            new FetchResult.RateLimited(null),
            new FetchResult.RateLimited(null),
            new FetchResult.Success(Snapshot),
            new FetchResult.RateLimited(null),
        ];
        var delays = new List<TimeSpan>();
        foreach (var result in results)
        {
            _client.Results.Enqueue(result);
            await _service.RefreshAsync(manual: false);
            delays.Add(_service.TimeUntilNextPoll);
            _clock.Advance(_service.TimeUntilNextPoll);
        }

        Assert.Equal(new[] { 20, 30, 30, 10, 20 }.Select(m => TimeSpan.FromMinutes(m)), delays);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    [InlineData(3, 30)]
    [InlineData(100, 30)]
    public void Backoff_is_exponential_and_capped_at_30_minutes(int consecutive, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), RefreshPolicy.Backoff(consecutive));
    }

    [Fact]
    public async Task Unauthorized_sets_token_expired_and_keeps_last_data()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        _client.Results.Enqueue(new FetchResult.Unauthorized());
        await _service.RefreshAsync(manual: false);
        _clock.Advance(TimeSpan.FromMinutes(10));

        var state = await _service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.TokenExpired, state.Status);
        Assert.Same(Snapshot, state.Snapshot);
        Assert.Equal(Start, state.LastUpdated);
        Assert.Equal(TimeSpan.FromMinutes(10), _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Failure_keeps_last_data_marked_stale()
    {
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        _client.Results.Enqueue(new FetchResult.Failed("network error"));
        await _service.RefreshAsync(manual: false);
        _clock.Advance(TimeSpan.FromMinutes(10));

        var state = await _service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.Error, state.Status);
        Assert.True(state.IsStale);
        Assert.Equal("network error", state.Error);
        Assert.Same(Snapshot, state.Snapshot);
        Assert.Equal(Start, state.LastUpdated);
    }

    [Fact]
    public async Task Missing_token_sets_no_credentials_without_request()
    {
        _token = null;

        var state = await _service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.NoCredentials, state.Status);
        Assert.Empty(_client.TokensSeen);
        Assert.Equal(TimeSpan.FromMinutes(10), _service.TimeUntilNextPoll);
    }

    [Fact]
    public async Task Unreadable_credentials_file_is_treated_as_transient_error()
    {
        var service = new UsageService(_client, () => throw new IOException("locked"), new RefreshPolicy(_clock));

        var state = await service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.Error, state.Status);
        Assert.Empty(_client.TokensSeen);
    }

    [Fact]
    public async Task Unauthorized_renews_once_and_retries_with_the_new_token()
    {
        var tokens = new FakeTokenSource { Lookup = new TokenLookup.Found("old"), Renewed = "new" };
        var service = new UsageService(_client, tokens, new RefreshPolicy(_clock));
        _client.Responder = token => token == "old" ? new FetchResult.Unauthorized() : new FetchResult.Success(Snapshot);

        var state = await service.RefreshAsync(manual: false);

        Assert.Equal(["old", "new"], _client.TokensSeen);
        Assert.Equal(UsageStatus.Ok, state.Status);
        Assert.Equal(["old"], tokens.RenewCalls);
    }

    [Fact]
    public async Task Unauthorized_without_renewal_is_token_expired()
    {
        var tokens = new FakeTokenSource { Lookup = new TokenLookup.Found("old") };
        var service = new UsageService(_client, tokens, new RefreshPolicy(_clock));
        _client.Results.Enqueue(new FetchResult.Unauthorized());

        var state = await service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.TokenExpired, state.Status);
        Assert.Single(_client.TokensSeen);
    }

    [Fact]
    public async Task Not_live_sends_nothing_and_keeps_data()
    {
        var tokens = new FakeTokenSource { Lookup = new TokenLookup.Found("tok") };
        var service = new UsageService(_client, tokens, new RefreshPolicy(_clock));
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        await service.RefreshAsync(manual: false);

        tokens.Lookup = new TokenLookup.NotLive();
        _clock.Advance(TimeSpan.FromMinutes(10));
        var state = await service.RefreshAsync(manual: false);

        Assert.Equal(UsageStatus.NotLive, state.Status);
        Assert.Same(Snapshot, state.Snapshot);
        Assert.Single(_client.TokensSeen);
    }

    [Fact]
    public void Seed_shows_last_known_usage_until_the_first_fetch()
    {
        _service.Seed(Snapshot, Start.AddHours(-2));

        Assert.Equal(UsageStatus.Loading, _service.State.Status);
        Assert.Same(Snapshot, _service.State.Snapshot);
        Assert.Equal(Start.AddHours(-2), _service.State.LastUpdated);
    }

    [Fact]
    public async Task Token_is_reread_on_every_poll()
    {
        _client.Results.Enqueue(new FetchResult.Unauthorized());
        _client.Results.Enqueue(new FetchResult.Success(Snapshot));
        await _service.RefreshAsync(manual: false);

        _token = "refreshed-by-claude-code";
        _clock.Advance(TimeSpan.FromMinutes(10));
        var state = await _service.RefreshAsync(manual: false);

        Assert.Equal(["tok", "refreshed-by-claude-code"], _client.TokensSeen);
        Assert.Equal(UsageStatus.Ok, state.Status);
    }

    private sealed class FakeTokenSource : IAccessTokenSource
    {
        public TokenLookup Lookup { get; set; } = new TokenLookup.NoCredentials();
        public string? Renewed { get; set; }
        public List<string> RenewCalls { get; } = [];

        public Task<TokenLookup> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Lookup);

        public Task<string?> RenewAsync(string rejectedToken, CancellationToken cancellationToken)
        {
            RenewCalls.Add(rejectedToken);
            return Task.FromResult(Renewed);
        }
    }
}
