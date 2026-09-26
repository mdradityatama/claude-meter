using ClaudeUsageTray.Core;

namespace ClaudeUsageTray.Tests;

public class CredentialsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("claude-usage-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Uses_user_profile_when_config_dir_is_not_set()
    {
        var path = CredentialsLocator.ResolvePath(name => name == "USERPROFILE" ? @"C:\Users\me" : null);

        Assert.Equal(@"C:\Users\me\.claude\.credentials.json", path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Uses_user_profile_when_config_dir_is_blank(string configDir)
    {
        var path = CredentialsLocator.ResolvePath(name => name switch
        {
            "CLAUDE_CONFIG_DIR" => configDir,
            "USERPROFILE" => @"C:\Users\me",
            _ => null,
        });

        Assert.Equal(@"C:\Users\me\.claude\.credentials.json", path);
    }

    [Fact]
    public void Uses_config_dir_when_set()
    {
        var path = CredentialsLocator.ResolvePath(name => name switch
        {
            "CLAUDE_CONFIG_DIR" => @"D:\claude-config",
            "USERPROFILE" => @"C:\Users\me",
            _ => null,
        });

        Assert.Equal(@"D:\claude-config\.credentials.json", path);
    }

    [Fact]
    public void Reads_access_token()
    {
        var path = Write("""{"claudeAiOauth":{"accessToken":"tok-123","refreshToken":"r"}}""");

        Assert.Equal("tok-123", CredentialsReader.ReadAccessToken(path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"claudeAiOauth":null}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":""}}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":5}}""")]
    [InlineData("not json")]
    public void Returns_null_when_token_is_unusable(string content)
    {
        Assert.Null(CredentialsReader.ReadAccessToken(Write(content)));
    }

    [Fact]
    public void Returns_null_when_file_is_missing()
    {
        Assert.Null(CredentialsReader.ReadAccessToken(Path.Combine(_dir, "missing.json")));
    }

    private string Write(string content)
    {
        var path = Path.Combine(_dir, ".credentials.json");
        File.WriteAllText(path, content);
        return path;
    }
}
