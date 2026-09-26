namespace ClaudeUsageTray.Core;

internal static class CredentialsLocator
{
    /// <summary>
    /// <c>%CLAUDE_CONFIG_DIR%\.credentials.json</c> if that variable is set, otherwise
    /// <c>%USERPROFILE%\.claude\.credentials.json</c>.
    /// </summary>
    public static string ResolvePath(Func<string, string?> getEnvironmentVariable)
    {
        var configDir = getEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(configDir))
        {
            var profile = getEnvironmentVariable("USERPROFILE")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configDir = Path.Combine(profile, ".claude");
        }

        return Path.Combine(configDir, ".credentials.json");
    }
}
