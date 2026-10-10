using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class TokenRefresherTests : IDisposable
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly AccountKey A = new("acc-a", "org-a");
    private static readonly AccountKey T = new("acc-t", "org-t");

    private readonly ClaudeHome _home = new();
    private readonly FakeOAuthTokenClient _tokens = new();
    private readonly TokenRefresher _refresher;
    private ClaudeHolder? _holder = new(A, "a-refresh");
    private bool _keepLive = true;

    public TokenRefresherTests()
    {
        SaveT(expiresAt: Start.AddMinutes(2));
        _refresher = new TokenRefresher(_home.Store, _tokens, () => _holder, () => _keepLive, _home.Clock);
    }

    public void Dispose() => _home.Dispose();

    private StoredAccount SaveT(DateTimeOffset expiresAt) =>
        _home.Save("acc-t", "org-t", "t@example.com",
            ClaudeJson.Credentials("t-access-1", "t-refresh-1", expiresAt, "org-t", Start.AddDays(20)), Start.AddDays(-1));

    private static TokenResult NewTokens(string? refresh = "t-refresh-2", IReadOnlyList<string>? scopes = null) =>
        new TokenResult.Success("t-access-2", TimeSpan.FromHours(8), refresh, scopes);

    [Fact]
    public async Task Refresh_splices_new_tokens_and_keeps_every_other_byte()
    {
        var before = _home.Store.Find(T)!.CredentialsJson;
        _tokens.Results.Enqueue(NewTokens(scopes: ["user:inference"]));

        Assert.Equal(RefreshOutcome.Refreshed, await _refresher.EnsureFreshAsync(T, default));

        var expected = JsonSplice.Replace(before, JsonSplice.EncodeString("t-access-2"), "claudeAiOauth", "accessToken");
        expected = JsonSplice.Replace(expected, JsonSplice.EncodeNumber((Start + TimeSpan.FromHours(8)).ToUnixTimeMilliseconds()), "claudeAiOauth", "expiresAt");
        expected = JsonSplice.Replace(expected, JsonSplice.EncodeString("t-refresh-2"), "claudeAiOauth", "refreshToken");
        expected = JsonSplice.Replace(expected, JsonSplice.EncodeStringArray(["user:inference"]), "claudeAiOauth", "scopes");
        Assert.Equal(expected, _home.Store.Find(T)!.CredentialsJson);
        Assert.False(_home.Store.HasUnsavedChanges);
        Assert.Equal(["t-refresh-1"], _tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Refresh_token_and_scopes_are_kept_when_not_returned()
    {
        var before = _home.Store.Find(T)!.Credentials!;
        _tokens.Results.Enqueue(NewTokens(refresh: null));

        await _refresher.EnsureFreshAsync(T, default);

        var after = _home.Store.Find(T)!;
        Assert.Equal("t-refresh-1", after.Credentials!.RefreshToken);
        Assert.Contains("\"scopes\":[\"user:inference\",\"user:profile\"]", System.Text.Encoding.UTF8.GetString(after.CredentialsJson));
        Assert.Equal(before.RefreshTokenExpiresAt, after.Credentials.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Fresh_token_is_not_refreshed()
    {
        SaveT(expiresAt: Start.AddMinutes(6));

        Assert.Equal(RefreshOutcome.Fresh, await _refresher.EnsureFreshAsync(T, default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Invalid_grant_marks_the_login_expired()
    {
        _tokens.Results.Enqueue(new TokenResult.InvalidGrant());

        Assert.Equal(RefreshOutcome.LoginExpired, await _refresher.EnsureFreshAsync(T, default));
        Assert.Equal(LoginState.Expired, _home.Store.Find(T)!.LoginState);
        Assert.False(_home.Store.HasUnsavedChanges);

        Assert.Equal(RefreshOutcome.LoginExpired, await _refresher.EnsureFreshAsync(T, default));
        Assert.Single(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Invalid_client_disables_refresh_for_all_accounts_once()
    {
        var disabled = 0;
        _refresher.Disabled += (_, _) => disabled++;
        _home.Save("acc-b", "org-b", "b@example.com", ClaudeJson.Credentials("b-access", "b-refresh", Start, "org-b"), Start);
        _tokens.Results.Enqueue(new TokenResult.InvalidClient());

        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(new AccountKey("acc-b", "org-b"), default));

        Assert.True(_refresher.GloballyDisabled);
        Assert.False(_refresher.CanRefresh);
        Assert.Equal(1, disabled);
        Assert.Single(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Transient_error_backs_off()
    {
        _tokens.Results.Enqueue(new TokenResult.Transient(null, "network error"));
        _tokens.Results.Enqueue(NewTokens());

        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Single(_tokens.RefreshTokensSeen);

        _home.Clock.Advance(RefreshPolicy.Backoff(1));
        Assert.Equal(RefreshOutcome.Refreshed, await _refresher.EnsureFreshAsync(T, default));
        Assert.Equal(2, _tokens.RefreshTokensSeen.Count);
    }

    [Fact]
    public async Task Active_account_is_never_refreshed()
    {
        _holder = new ClaudeHolder(T, null);

        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Account_whose_refresh_token_is_live_is_never_refreshed()
    {
        _holder = new ClaudeHolder(A, "t-refresh-1");

        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Unknown_holder_blocks_refresh()
    {
        _holder = null;

        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Toggle_off_sends_nothing()
    {
        _keepLive = false;

        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.EnsureFreshAsync(T, default));
        Assert.Empty(_tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Failed_save_blocks_further_refreshes()
    {
        File.Delete(_home.Paths.Store);
        Directory.CreateDirectory(_home.Paths.Store);
        _tokens.Results.Enqueue(NewTokens());
        _tokens.Results.Enqueue(NewTokens(refresh: "t-refresh-3"));

        Assert.Equal(RefreshOutcome.Refreshed, await _refresher.EnsureFreshAsync(T, default));
        Assert.True(_home.Store.HasUnsavedChanges);
        Assert.Equal("t-access-2", _home.Store.Find(T)!.Credentials!.AccessToken);

        _home.Clock.Advance(RefreshPolicy.PollInterval);
        Assert.Equal(RefreshOutcome.Unavailable, await _refresher.ForceRefreshAsync(T, default));
        Assert.Single(_tokens.RefreshTokensSeen);

        Directory.Delete(_home.Paths.Store);
        Assert.True(_home.Store.TrySave());
        _home.Clock.Advance(RefreshPolicy.PollInterval);
        Assert.Equal(RefreshOutcome.Refreshed, await _refresher.ForceRefreshAsync(T, default));
        Assert.Equal(["t-refresh-1", "t-refresh-2"], _tokens.RefreshTokensSeen);
    }

    [Fact]
    public async Task Pending_unsaved_changes_do_not_block_refresh()
    {
        _home.Store.Upsert(_home.Store.Find(T)! with { LastKnownAt = Start });
        _tokens.Results.Enqueue(NewTokens());

        Assert.Equal(RefreshOutcome.Refreshed, await _refresher.EnsureFreshAsync(T, default));
    }

    [Fact]
    public async Task Force_refresh_ignores_the_margin()
    {
        SaveT(expiresAt: Start.AddHours(6));
        _tokens.Results.Enqueue(NewTokens());

        Assert.Equal(RefreshOutcome.Refreshed, await _refresher.ForceRefreshAsync(T, default));
    }

    [Fact]
    public void Holder_reads_claude_codes_files()
    {
        _home.WriteLive(ClaudeJson.Credentials("x", "a-refresh", Start, "org-a"), ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com"));

        Assert.Equal(new ClaudeHolder(A, "a-refresh"), ClaudeHolder.Read(_home.Paths));
    }
}
