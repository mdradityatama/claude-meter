namespace ClaudeMeter.Core;

/// <summary>Every path Claude Meter reads, writes or locks, resolved once at startup.</summary>
internal sealed record ClaudePaths(
    string ConfigHome,
    string Credentials,
    string GlobalConfig,
    string OAuthRefreshLock,
    string LegacyLock,
    string StorageWriteLock,
    string GlobalConfigLock,
    string AppData)
{
    public string Store => Path.Combine(AppData, "accounts.dat");

    public string Settings => Path.Combine(AppData, "settings.json");

    public static ClaudePaths ForCurrentUser() => Resolve(
        Environment.GetEnvironmentVariable,
        File.Exists,
        RealPath,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static ClaudePaths Resolve(Func<string, string?> env, Func<string, bool> fileExists, Func<string, string> realPath, string localAppData)
    {
        var configDir = env("CLAUDE_CONFIG_DIR");
        var hasConfigDir = !string.IsNullOrWhiteSpace(configDir);
        var profile = env("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configHome = hasConfigDir ? configDir! : Path.Combine(profile, ".claude");

        var legacyConfig = Path.Combine(configHome, ".config.json");
        var globalConfig = fileExists(legacyConfig)
            ? legacyConfig
            : Path.Combine(hasConfigDir ? configDir! : profile, ".claude.json");

        return new ClaudePaths(
            configHome,
            Path.Combine(configHome, ".credentials.json"),
            globalConfig,
            Path.Combine(configHome, ".oauth_refresh.lock"),
            Path.TrimEndingDirectorySeparator(realPath(configHome)) + ".lock",
            Path.Combine(configHome, ".storage-write.lock"),
            globalConfig + ".lock",
            Path.Combine(localAppData, "ClaudeMeter"));
    }

    /// <summary>Like Node's fs.realpath, which Claude Code uses for its legacy lock: the final link target, else the full path.</summary>
    public static string RealPath(string directory)
    {
        var info = new DirectoryInfo(directory);
        return info.Exists ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName : info.FullName;
    }
}
