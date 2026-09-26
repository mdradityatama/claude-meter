using System.Text.Json;

namespace ClaudeUsageTray.Core;

/// <summary>
/// Tolerant parser for the undocumented /api/oauth/usage response. Only <c>five_hour</c> and
/// <c>seven_day</c> are read; anything missing, null or of an unexpected type becomes null.
/// </summary>
internal static class UsageParser
{
    /// <exception cref="FormatException">The body is not a JSON object.</exception>
    public static UsageSnapshot Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new FormatException("Usage response is not a JSON object.");

            return new UsageSnapshot(
                ReadWindow(doc.RootElement, "five_hour"),
                ReadWindow(doc.RootElement, "seven_day"));
        }
        catch (JsonException ex)
        {
            throw new FormatException("Usage response is not valid JSON.", ex);
        }
    }

    private static UsageWindow? ReadWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
            return null;

        double? utilization =
            window.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetDouble(out var d)
                ? d
                : null;

        DateTimeOffset? resetsAt =
            window.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String && r.TryGetDateTimeOffset(out var t)
                ? t
                : null;

        return new UsageWindow(utilization, resetsAt);
    }
}
