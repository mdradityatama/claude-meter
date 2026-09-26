using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class FakeUsageClient : IUsageClient
{
    public Queue<FetchResult> Results { get; } = new();
    public List<string> TokensSeen { get; } = [];

    public Task<FetchResult> FetchAsync(string accessToken, CancellationToken cancellationToken)
    {
        TokensSeen.Add(accessToken);
        return Task.FromResult(Results.Dequeue());
    }
}
