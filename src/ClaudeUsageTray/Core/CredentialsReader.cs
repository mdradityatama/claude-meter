using System.Text.Json;

namespace ClaudeUsageTray.Core;

/// <summary>
/// Reads <c>claudeAiOauth.accessToken</c> from Claude Code's credentials file.
/// The token is returned to the caller only; it is never cached, logged or written anywhere.
/// </summary>
internal static class CredentialsReader
{
    /// <returns>The token, or null if the file is missing or holds no usable token.</returns>
    /// <exception cref="IOException">The file exists but could not be read (e.g. locked); treat as transient.</exception>
    public static string? ReadAccessToken(string path)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                && oauth.ValueKind == JsonValueKind.Object
                && oauth.TryGetProperty("accessToken", out var token)
                && token.ValueKind == JsonValueKind.String
                && token.GetString() is { Length: > 0 } value
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
