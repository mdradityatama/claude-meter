namespace ClaudeUsageTray.Core;

/// <summary>One usage window. Any value may be unknown.</summary>
internal sealed record UsageWindow(double? Utilization, DateTimeOffset? ResetsAt);

internal sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? SevenDay);
