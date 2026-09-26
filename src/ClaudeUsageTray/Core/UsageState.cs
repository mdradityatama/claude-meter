namespace ClaudeUsageTray.Core;

public enum UsageStatus
{
    Loading,
    Ok,
    Error,
    TokenExpired,
    NoCredentials,
}

/// <param name="Snapshot">Last successfully fetched data; kept across failures.</param>
/// <param name="LastUpdated">When <paramref name="Snapshot"/> was fetched.</param>
/// <param name="Error">Short, generic reason for a non-Ok status. Never contains the token.</param>
internal sealed record UsageState(UsageStatus Status, UsageSnapshot? Snapshot, DateTimeOffset? LastUpdated, string? Error)
{
    public static UsageState Initial { get; } = new(UsageStatus.Loading, null, null, null);

    /// <summary>Showing old data because the latest attempt failed.</summary>
    public bool IsStale => Status != UsageStatus.Ok && Snapshot is not null;
}
