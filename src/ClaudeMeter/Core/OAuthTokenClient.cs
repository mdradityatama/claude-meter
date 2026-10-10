using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaudeMeter.Core;

internal abstract record TokenResult
{
    private TokenResult() { }

    public sealed record Success(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken, IReadOnlyList<string>? Scopes) : TokenResult;
    /// <summary>The refresh token is dead: the login expired or was revoked.</summary>
    public sealed record InvalidGrant : TokenResult;
    /// <summary>Anthropic rejected Claude Code's client id for us.</summary>
    public sealed record InvalidClient : TokenResult;
    /// <param name="Reason">Short, generic; never includes response or exception text.</param>
    public sealed record Transient(RetryConditionHeaderValue? RetryAfter, string Reason) : TokenResult;
}

internal interface IOAuthTokenClient
{
    Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>Claude Code's token endpoint, used only for accounts Claude Code does not hold.</summary>
internal sealed class OAuthTokenClient : IOAuthTokenClient, IDisposable
{
    // The refresh token is only ever sent here. Redirects are disabled so it cannot follow one elsewhere.
    private static readonly Uri Endpoint = new("https://platform.claude.com/v1/oauth/token");
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public OAuthTokenClient() => _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeMeter/{AppInfo.Version}");

    public async Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(Endpoint, body, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => TokenResponseParser.ParseSuccess(text),
                HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized => TokenResponseParser.ParseError(text),
                HttpStatusCode.TooManyRequests => new TokenResult.Transient(response.Headers.RetryAfter, "rate limited"),
                _ => new TokenResult.Transient(null, $"HTTP {(int)response.StatusCode}"),
            };
        }
        catch (HttpRequestException)
        {
            return new TokenResult.Transient(null, "network error");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TokenResult.Transient(null, "timeout");
        }
    }

    public void Dispose() => _http.Dispose();
}

internal static class TokenResponseParser
{
    public static TokenResult ParseSuccess(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || CredentialsInfo.Text(root, "access_token") is not { Length: > 0 } accessToken
                || !root.TryGetProperty("expires_in", out var expires) || expires.ValueKind != JsonValueKind.Number
                || !expires.TryGetInt64(out var seconds) || seconds <= 0)
                return Invalid();

            IReadOnlyList<string>? scopes = root.TryGetProperty("scope", out var scope) ? scope.ValueKind switch
            {
                JsonValueKind.String => scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                JsonValueKind.Array => [.. scope.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!)],
                _ => null,
            } : null;

            return new TokenResult.Success(accessToken, TimeSpan.FromSeconds(seconds), CredentialsInfo.Text(root, "refresh_token"), scopes);
        }
        catch (JsonException)
        {
            return Invalid();
        }

        static TokenResult Invalid() => new TokenResult.Transient(null, "invalid response");
    }

    public static TokenResult ParseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.ValueKind == JsonValueKind.Object ? CredentialsInfo.Text(doc.RootElement, "error") : null) switch
            {
                "invalid_grant" => new TokenResult.InvalidGrant(),
                "invalid_client" => new TokenResult.InvalidClient(),
                _ => new TokenResult.Transient(null, "HTTP 400"),
            };
        }
        catch (JsonException)
        {
            return new TokenResult.Transient(null, "HTTP 400");
        }
    }
}
