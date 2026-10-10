using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class LoginFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("claude-meter-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] Config(string oauthAccount, int startups = 12) => U(
        "{\n  \"numStartups\": " + startups + ",\n  \"oauthAccount\": " + oauthAccount + ",\n  \"projects\": {}\n}");

    private const string AccountA =
        "{\n    \"accountUuid\": \"acc-a\",\n    \"emailAddress\": \"a@example.com\",\n    \"organizationUuid\": \"org-a\",\n    \"displayName\": \"Dimas Ü ✓\"\n  }";

    [Fact]
    public void Parses_credentials()
    {
        var info = CredentialsInfo.Parse(U(
            """{"claudeAiOauth":{"accessToken":"tok","refreshToken":"ref","expiresAt":1791700000000,"refreshTokenExpiresAt":1792900000000,"scopes":["a"],"subscriptionType":"pro"},"organizationUuid":"org-a"}"""));

        Assert.Equal(
            new CredentialsInfo(
                "tok",
                "ref",
                DateTimeOffset.FromUnixTimeMilliseconds(1791700000000),
                DateTimeOffset.FromUnixTimeMilliseconds(1792900000000),
                "pro",
                "org-a"),
            info);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"claudeAiOauth":null}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":""}}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":5}}""")]
    [InlineData("not json")]
    public void Credentials_without_usable_token_are_null(string content)
    {
        Assert.Null(CredentialsInfo.Parse(U(content)));
    }

    [Fact]
    public void Out_of_range_expiry_is_null()
    {
        var info = CredentialsInfo.Parse(U("""{"claudeAiOauth":{"accessToken":"tok","expiresAt":9000000000000000000}}"""));

        Assert.NotNull(info);
        Assert.Null(info.ExpiresAt);
    }

    [Fact]
    public void Parses_oauth_account_and_keeps_raw_bytes()
    {
        var account = OAuthAccount.Parse(Config(AccountA));

        Assert.NotNull(account);
        Assert.Equal(new AccountKey("acc-a", "org-a"), account.Key);
        Assert.Equal("a@example.com", account.Email);
        Assert.Equal(U(AccountA), account.Raw);
    }

    [Theory]
    [InlineData("{\"numStartups\": 1}")]
    [InlineData("{\"oauthAccount\": {\"accountUuid\": \"acc-a\"}}")]
    [InlineData("{\"oauthAccount\": null}")]
    public void Oauth_account_missing_or_without_uuids_is_null(string config)
    {
        Assert.Null(OAuthAccount.Parse(U(config)));
    }

    [Fact]
    public void Duplicate_oauth_account_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => OAuthAccount.Parse(U("""{"oauthAccount":{},"oauthAccount":{}}""")));
    }

    [Fact]
    public void Missing_files_read_as_null()
    {
        var paths = ClaudePaths.Resolve(name => name == "USERPROFILE" ? _dir : null, File.Exists, p => p, _dir);

        var files = LiveFiles.Read(paths);

        Assert.Null(files.Credentials);
        Assert.Null(files.GlobalConfig);
        Assert.Null(LiveLogin.From(files));
        Assert.Null(LiveFiles.ReadAccessToken(paths));
    }

    [Fact]
    public void Reads_the_live_access_token()
    {
        var paths = ClaudePaths.Resolve(name => name == "USERPROFILE" ? _dir : null, File.Exists, p => p, _dir);
        Directory.CreateDirectory(paths.ConfigHome);
        File.WriteAllText(paths.Credentials, """{"claudeAiOauth":{"accessToken":"tok-123","refreshToken":"r"}}""");

        Assert.Equal("tok-123", LiveFiles.ReadAccessToken(paths));
    }

    [Fact]
    public void Same_as_ignores_other_config_changes()
    {
        var credentials = U("""{"claudeAiOauth":{"accessToken":"tok","refreshToken":"ref"}}""");
        var login = LiveLogin.From(new LiveFiles(credentials, Config(AccountA)))!;

        Assert.True(login.SameAs(LiveLogin.From(new LiveFiles(credentials, Config(AccountA, startups: 13)))!));
        Assert.False(login.SameAs(LiveLogin.From(new LiveFiles(credentials, Config(AccountA.Replace("Dimas", "Other"))))!));
        Assert.False(login.SameAs(LiveLogin.From(new LiveFiles(U("""{"claudeAiOauth":{"accessToken":"tok2","refreshToken":"ref"}}"""), Config(AccountA)))!));
    }
}
