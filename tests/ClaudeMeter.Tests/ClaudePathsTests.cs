using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class ClaudePathsTests
{
    private const string LocalAppData = @"C:\Users\me\AppData\Local";

    private static ClaudePaths Resolve(Func<string, string?> env, Func<string, bool>? fileExists = null, Func<string, string>? realPath = null) =>
        ClaudePaths.Resolve(env, fileExists ?? (_ => false), realPath ?? (p => p), LocalAppData);

    private static string? ProfileOnly(string name) => name == "USERPROFILE" ? @"C:\Users\me" : null;

    [Fact]
    public void Defaults_to_user_profile()
    {
        var paths = Resolve(ProfileOnly);

        Assert.Equal(@"C:\Users\me\.claude", paths.ConfigHome);
        Assert.Equal(@"C:\Users\me\.claude\.credentials.json", paths.Credentials);
        Assert.Equal(@"C:\Users\me\.claude.json", paths.GlobalConfig);
        Assert.Equal(@"C:\Users\me\.claude\.oauth_refresh.lock", paths.OAuthRefreshLock);
        Assert.Equal(@"C:\Users\me\.claude.lock", paths.LegacyLock);
        Assert.Equal(@"C:\Users\me\.claude\.storage-write.lock", paths.StorageWriteLock);
        Assert.Equal(@"C:\Users\me\.claude.json.lock", paths.GlobalConfigLock);
        Assert.Equal(@"C:\Users\me\AppData\Local\ClaudeMeter\accounts.dat", paths.Store);
        Assert.Equal(@"C:\Users\me\AppData\Local\ClaudeMeter\settings.json", paths.Settings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_config_dir_is_ignored(string configDir)
    {
        var paths = Resolve(name => name switch
        {
            "CLAUDE_CONFIG_DIR" => configDir,
            "USERPROFILE" => @"C:\Users\me",
            _ => null,
        });

        Assert.Equal(@"C:\Users\me\.claude\.credentials.json", paths.Credentials);
        Assert.Equal(@"C:\Users\me\.claude.json", paths.GlobalConfig);
    }

    [Fact]
    public void Config_dir_moves_everything()
    {
        var paths = Resolve(name => name switch
        {
            "CLAUDE_CONFIG_DIR" => @"D:\cc",
            "USERPROFILE" => @"C:\Users\me",
            _ => null,
        });

        Assert.Equal(@"D:\cc", paths.ConfigHome);
        Assert.Equal(@"D:\cc\.credentials.json", paths.Credentials);
        Assert.Equal(@"D:\cc\.claude.json", paths.GlobalConfig);
        Assert.Equal(@"D:\cc\.oauth_refresh.lock", paths.OAuthRefreshLock);
        Assert.Equal(@"D:\cc.lock", paths.LegacyLock);
        Assert.Equal(@"D:\cc\.claude.json.lock", paths.GlobalConfigLock);
    }

    [Fact]
    public void Legacy_config_json_wins_when_present()
    {
        var paths = Resolve(ProfileOnly, fileExists: path => path == @"C:\Users\me\.claude\.config.json");

        Assert.Equal(@"C:\Users\me\.claude\.config.json", paths.GlobalConfig);
        Assert.Equal(@"C:\Users\me\.claude\.config.json.lock", paths.GlobalConfigLock);
    }

    [Fact]
    public void Legacy_lock_uses_the_real_path()
    {
        var paths = Resolve(ProfileOnly, realPath: path => path == @"C:\Users\me\.claude" ? @"E:\real\claude\" : path);

        Assert.Equal(@"E:\real\claude.lock", paths.LegacyLock);
    }
}
