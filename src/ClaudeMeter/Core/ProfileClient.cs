using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeMeter.Core;

internal abstract record ProfileResult
{
    private ProfileResult() { }

    public sealed record Found(string AccountUuid, string? OrganizationUuid) : ProfileResult;

    /// <summary>Expired token, 401, offline, or a response without <c>account.uuid</c>.</summary>
    public sealed record Unavailable(string Reason) : ProfileResult;
}

internal interface IProfileClient
{
    Task<ProfileResult> GetAsync(string accessToken, CancellationToken cancellationToken);
}

/// <summary>Asks Anthropic whose token this is. Never called while holding a lock.</summary>
internal sealed class ProfileClient : IProfileClient, IDisposable
{
    // The token is only ever sent here. Redirects are disabled so it cannot follow one elsewhere.
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/profile");

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task<ProfileResult> GetAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK
                ? ProfileParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
                : new ProfileResult.Unavailable($"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException)
        {
            return new ProfileResult.Unavailable("network error");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProfileResult.Unavailable("timeout");
        }
    }

    public void Dispose() => _http.Dispose();
}

internal static class ProfileParser
{
    public static ProfileResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object
                || CredentialsInfo.Text(account, "uuid") is not { Length: > 0 } accountUuid)
                return new ProfileResult.Unavailable("unexpected response");

            var org = root.TryGetProperty("organization", out var o) && o.ValueKind == JsonValueKind.Object
                ? CredentialsInfo.Text(o, "uuid")
                : null;
            return new ProfileResult.Found(accountUuid, org);
        }
        catch (JsonException)
        {
            return new ProfileResult.Unavailable("unexpected response");
        }
    }
}

internal static class ProfileCheck
{
    /// <summary>Capture verification step 3. Skips the request when the access token is already expired.</summary>
    public static Task<ProfileResult> RunAsync(IProfileClient client, CredentialsInfo credentials, TimeProvider clock, CancellationToken cancellationToken) =>
        credentials.ExpiresAt is { } expiresAt && expiresAt > clock.GetUtcNow()
            ? client.GetAsync(credentials.AccessToken, cancellationToken)
            : Task.FromResult<ProfileResult>(new ProfileResult.Unavailable("token expired"));
}
