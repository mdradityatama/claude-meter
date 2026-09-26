using System.Net;
using System.Net.Http.Headers;

namespace ClaudeMeter.Core;

internal abstract record FetchResult
{
    private FetchResult() { }

    public sealed record Success(UsageSnapshot Snapshot) : FetchResult;
    public sealed record Unauthorized : FetchResult;
    public sealed record RateLimited(RetryConditionHeaderValue? RetryAfter) : FetchResult;
    /// <param name="Reason">Short, generic description; never includes exception text.</param>
    public sealed record Failed(string Reason) : FetchResult;
}

internal interface IUsageClient
{
    Task<FetchResult> FetchAsync(string accessToken, CancellationToken cancellationToken);
}

/// <summary>Calls the unofficial usage endpoint that Claude Code itself uses.</summary>
internal sealed class UsageClient : IUsageClient, IDisposable
{
    // The token is only ever sent here. Redirects are disabled so it cannot follow one elsewhere.
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public async Task<FetchResult> FetchAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    try
                    {
                        return new FetchResult.Success(UsageParser.Parse(body));
                    }
                    catch (FormatException)
                    {
                        return new FetchResult.Failed("invalid response");
                    }
                case HttpStatusCode.Unauthorized:
                    return new FetchResult.Unauthorized();
                case HttpStatusCode.TooManyRequests:
                    return new FetchResult.RateLimited(response.Headers.RetryAfter);
                default:
                    return new FetchResult.Failed($"HTTP {(int)response.StatusCode}");
            }
        }
        catch (HttpRequestException)
        {
            return new FetchResult.Failed("network error");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchResult.Failed("timeout");
        }
    }

    public void Dispose() => _http.Dispose();
}
