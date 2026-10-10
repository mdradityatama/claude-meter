using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class CaptureVerifierTests
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly ProfileResult FoundA = new ProfileResult.Found("acc-a", "org-a");
    private static readonly ProfileResult Offline = new ProfileResult.Unavailable("offline");

    private static LiveLogin Live(string refresh, string credentialsOrg = "org-a") => Login("acc-a", "org-a", "a@example.com",
        ClaudeJson.Credentials("a-access", refresh, Start.AddHours(8), credentialsOrg));

    private static LiveLogin Login(string account, string org, string email, byte[] credentials) =>
        LiveLogin.From(new LiveFiles(credentials, ClaudeJson.GlobalConfig(account, org, email)))!;

    private static StoredAccount Stored(string account, string org, string refresh) =>
        StoredAccount.Create(Login(account, org, $"{account}@example.com", ClaudeJson.Credentials("x", refresh, Start, org)), Start);

    private static CaptureVerdict Verdict(LiveLogin live, ProfileResult profile, params StoredAccount[] stored) =>
        CaptureVerifier.Evaluate(live, stored, profile).Verdict;

    [Fact]
    public void Verified_new_login_is_accepted() => Assert.Equal(CaptureVerdict.Accept, Verdict(Live("r-1"), FoundA));

    [Fact]
    public void Credentials_of_another_organization_are_rejected() =>
        Assert.Equal(CaptureVerdict.Reject, Verdict(Live("r-1", credentialsOrg: "org-x"), FoundA));

    [Fact]
    public void Credentials_without_organization_skip_check_1()
    {
        var credentials = Encoding.UTF8.GetString(ClaudeJson.Credentials("a-access", "r-1", Start.AddHours(8), "org-a"))
            .Replace(""","organizationUuid":"org-a"}""", "}");
        var live = Login("acc-a", "org-a", "a@example.com", Encoding.UTF8.GetBytes(credentials));

        Assert.Null(live.Credentials.OrganizationUuid);
        Assert.Equal(CaptureVerdict.Accept, Verdict(live, FoundA));
    }

    [Fact]
    public void Refresh_token_of_another_saved_account_is_rejected() =>
        Assert.Equal(CaptureVerdict.Reject, Verdict(Live("r-1"), FoundA, Stored("acc-t", "org-t", "r-1")));

    [Fact]
    public void Own_old_snapshot_with_same_refresh_token_is_fine() =>
        Assert.Equal(CaptureVerdict.Accept, Verdict(Live("r-1"), FoundA, Stored("acc-a", "org-a", "r-1")));

    [Fact]
    public void Profile_account_mismatch_is_rejected() =>
        Assert.Equal(CaptureVerdict.Reject, Verdict(Live("r-1"), new ProfileResult.Found("acc-x", "org-a")));

    [Fact]
    public void Profile_organization_mismatch_is_rejected() =>
        Assert.Equal(CaptureVerdict.Reject, Verdict(Live("r-1"), new ProfileResult.Found("acc-a", "org-x")));

    [Fact]
    public void Profile_without_organization_is_accepted() =>
        Assert.Equal(CaptureVerdict.Accept, Verdict(Live("r-1"), new ProfileResult.Found("acc-a", null)));

    [Fact]
    public void Unverified_new_login_is_deferred() => Assert.Equal(CaptureVerdict.Defer, Verdict(Live("r-1"), Offline));

    [Fact]
    public void Unverified_known_login_is_accepted() =>
        Assert.Equal(CaptureVerdict.Accept, Verdict(Live("r-2"), Offline, Stored("acc-a", "org-a", "r-1")));

    [Fact]
    public void Missing_refresh_token_is_rejected()
    {
        var live = Login("acc-a", "org-a", "a@example.com", Encoding.UTF8.GetBytes("""{"claudeAiOauth":{"accessToken":"a"}}"""));

        Assert.Equal(CaptureVerdict.Reject, Verdict(live, FoundA));
    }

    [Fact]
    public async Task Profile_check_skips_the_request_for_an_expired_token()
    {
        var client = new FakeProfileClient { Result = FoundA };
        var clock = new FakeTimeProvider(Start);
        var expired = CredentialsInfo.Parse(ClaudeJson.Credentials("a", "r", Start.AddSeconds(-1), "org-a"))!;

        var result = await ProfileCheck.RunAsync(client, expired, clock, default);

        Assert.Equal(new ProfileResult.Unavailable("token expired"), result);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Profile_check_asks_for_a_valid_token()
    {
        var client = new FakeProfileClient { Result = FoundA };
        var valid = CredentialsInfo.Parse(ClaudeJson.Credentials("a-access", "r", Start.AddHours(1), "org-a"))!;

        Assert.Equal(FoundA, await ProfileCheck.RunAsync(client, valid, new FakeTimeProvider(Start), default));
        Assert.Equal(["a-access"], client.Calls);
    }
}
