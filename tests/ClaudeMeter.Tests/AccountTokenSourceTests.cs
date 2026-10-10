using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AccountTokenSourceTests : IDisposable
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly AccountKey A = new("acc-a", "org-a");
    private static readonly AccountKey T = new("acc-t", "org-t");

    private readonly ClaudeHome _home = new();
    private readonly FakeOAuthTokenClient _tokens = new();
    private readonly TokenRefresher _refresher;
    private AccountKey? _active = A;
    private bool _keepLive = true;

    public AccountTokenSourceTests()
    {
        _refresher = new TokenRefresher(_home.Store, _tokens, () => new ClaudeHolder(_active, "live-refresh"), () => _keepLive, _home.Clock);
    }

    public void Dispose() => _home.Dispose();

    private AccountTokenSource Source(AccountKey key, string? liveToken = "live") =>
        new(key, () => _active, () => liveToken, _home.Store, _refresher, new OperationGate(), _home.Clock);

    private void SaveT(DateTimeOffset expiresAt) => _home.Save("acc-t", "org-t", "t@example.com",
        ClaudeJson.Credentials("t-access-1", "t-refresh-1", expiresAt, "org-t"), Start);

    [Fact]
    public async Task Active_account_uses_the_live_token_and_never_renews()
    {
        var source = Source(A);

        Assert.Equal(new TokenLookup.Found("live"), await source.GetAsync(default));
        Assert.Null(await source.RenewAsync("live", default));
        Assert.Equal(new TokenLookup.NoCredentials(), await Source(A, liveToken: null).GetAsync(default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Inactive_account_uses_its_saved_token()
    {
        SaveT(Start.AddHours(6));

        Assert.Equal(new TokenLookup.Found("t-access-1"), await Source(T).GetAsync(default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Inactive_near_expiry_is_refreshed_first()
    {
        SaveT(Start.AddMinutes(2));
        _tokens.Results.Enqueue(new TokenResult.Success("t-access-2", TimeSpan.FromHours(8), null, null));

        Assert.Equal(new TokenLookup.Found("t-access-2"), await Source(T).GetAsync(default));
    }

    [Fact]
    public async Task Without_refresh_polling_stops_at_expires_at()
    {
        _keepLive = false;
        SaveT(Start.AddSeconds(-1));

        Assert.Equal(new TokenLookup.NotLive(), await Source(T).GetAsync(default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Without_refresh_polling_stops_after_a_401()
    {
        _keepLive = false;
        SaveT(Start.AddHours(6));
        var source = Source(T);

        Assert.Null(await source.RenewAsync("t-access-1", default));

        Assert.Equal(new TokenLookup.NotLive(), await source.GetAsync(default));
    }

    [Fact]
    public async Task With_refresh_a_401_refreshes_once()
    {
        SaveT(Start.AddHours(6));
        _tokens.Results.Enqueue(new TokenResult.Success("t-access-2", TimeSpan.FromHours(8), "t-refresh-2", null));

        Assert.Equal("t-access-2", await Source(T).RenewAsync("t-access-1", default));
        Assert.Equal(["t-refresh-1"], _tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Expired_login_is_not_live()
    {
        SaveT(Start.AddHours(6));
        _home.Store.Upsert(_home.Store.Find(T)! with { LoginState = LoginState.Expired });

        Assert.Equal(new TokenLookup.NotLive(), await Source(T).GetAsync(default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }
}
