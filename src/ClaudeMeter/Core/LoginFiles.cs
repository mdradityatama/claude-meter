using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>Claude Code compares logins by this pair; one person can have several organizations.</summary>
internal readonly record struct AccountKey(string AccountUuid, string OrganizationUuid);

/// <summary>The fields Claude Meter reads from <c>.credentials.json</c>. Tokens go only to Anthropic.</summary>
internal sealed record CredentialsInfo(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    string? SubscriptionType,
    string? OrganizationUuid)
{
    /// <returns>Null if the content is not JSON or holds no usable access token.</returns>
    public static CredentialsInfo? Parse(byte[] content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || Text(oauth, "accessToken") is not { Length: > 0 } accessToken)
                return null;

            return new CredentialsInfo(
                accessToken,
                Text(oauth, "refreshToken"),
                Millis(oauth, "expiresAt"),
                Millis(oauth, "refreshTokenExpiresAt"),
                Text(oauth, "subscriptionType"),
                Text(root, "organizationUuid"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Millis(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var ms) && ms >= 0 && ms <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}

/// <param name="Raw">The exact bytes of the <c>oauthAccount</c> value, used verbatim when switching.</param>
internal sealed record OAuthAccount(AccountKey Key, string Email, byte[] Raw)
{
    /// <returns>Null if <c>oauthAccount</c> is absent or lacks either UUID.</returns>
    /// <exception cref="FormatException">Invalid JSON or a duplicate <c>oauthAccount</c>.</exception>
    public static OAuthAccount? Parse(byte[] globalConfig)
    {
        if (JsonSplice.Find(globalConfig, "oauthAccount") is not { } span)
            return null;

        var raw = globalConfig.AsSpan(span.Start, span.Length).ToArray();
        using var doc = JsonDocument.Parse(raw);
        var e = doc.RootElement;
        return e.ValueKind == JsonValueKind.Object
            && CredentialsInfo.Text(e, "accountUuid") is { Length: > 0 } account
            && CredentialsInfo.Text(e, "organizationUuid") is { Length: > 0 } org
                ? new OAuthAccount(new AccountKey(account, org), CredentialsInfo.Text(e, "emailAddress") ?? "", raw)
                : null;
    }
}

/// <summary>Raw bytes of Claude Code's credentials file and global config.</summary>
internal sealed record LiveFiles(byte[]? Credentials, byte[]? GlobalConfig)
{
    /// <exception cref="IOException">A file exists but could not be read; treat as transient.</exception>
    public static LiveFiles Read(ClaudePaths paths) => new(ReadOrNull(paths.Credentials), ReadOrNull(paths.GlobalConfig));

    /// <summary>The active account's access token, re-read on every call (Claude Code refreshes it).</summary>
    public static string? ReadAccessToken(ClaudePaths paths) =>
        ReadOrNull(paths.Credentials) is { } bytes ? CredentialsInfo.Parse(bytes)?.AccessToken : null;

    public static byte[]? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>A signed-in Claude Code: both files present and parseable.</summary>
internal sealed record LiveLogin(byte[] CredentialsJson, CredentialsInfo Credentials, OAuthAccount Account)
{
    /// <returns>Null if either file is missing or holds no login.</returns>
    /// <exception cref="FormatException">The global config is invalid JSON or has a duplicate <c>oauthAccount</c>.</exception>
    public static LiveLogin? From(LiveFiles files) =>
        files.Credentials is { } credentials && files.GlobalConfig is { } config
        && CredentialsInfo.Parse(credentials) is { } info && OAuthAccount.Parse(config) is { } account
            ? new LiveLogin(credentials, info, account)
            : null;

    /// <summary>Same login bytes. The rest of the global config is ignored: Claude Code rewrites it constantly.</summary>
    public bool SameAs(LiveLogin other) =>
        CredentialsJson.AsSpan().SequenceEqual(other.CredentialsJson) && Account.Raw.AsSpan().SequenceEqual(other.Account.Raw);
}
