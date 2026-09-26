using System.Globalization;

namespace ClaudeUsageTray.Core;

public enum UsageLevel
{
    Unknown,
    Green,
    Yellow,
    Red,
    Error,
}

internal sealed record IconFace(string Text, UsageLevel Level);

/// <summary>Turns a <see cref="UsageState"/> into the icon face, tooltip and menu info lines.</summary>
internal static class DisplayFormatter
{
    public const int MaxTooltipLength = 127;
    private const string Unknown = "—";

    /// <summary>Based on the displayed (rounded) percentage so number and color always agree.</summary>
    public static UsageLevel LevelFor(int percent) =>
        percent >= 80 ? UsageLevel.Red
        : percent >= 50 ? UsageLevel.Yellow
        : UsageLevel.Green;

    public static IconFace Icon(UsageState state)
    {
        if (state.Status is UsageStatus.TokenExpired or UsageStatus.NoCredentials
            || (state.Status == UsageStatus.Error && state.Snapshot is null))
            return new IconFace("!", UsageLevel.Error);

        if (Round(state.Snapshot?.FiveHour?.Utilization) is not int percent)
            return new IconFace(Unknown, UsageLevel.Unknown);

        return percent >= 100
            ? new IconFace("F", UsageLevel.Red)
            : new IconFace(percent.ToString(CultureInfo.InvariantCulture), LevelFor(percent));
    }

    public static string Tooltip(UsageState state, TimeZoneInfo zone, DateTimeOffset now)
    {
        string text;
        if (state.Snapshot is not { } snapshot)
        {
            text = "Claude usage: " + StatusText(state);
        }
        else
        {
            text = $"5h {Percent(snapshot.FiveHour)} · reset {FormatTime(snapshot.FiveHour?.ResetsAt, zone, now)}"
                + $" | 7d {Percent(snapshot.SevenDay)} · reset {FormatTime(snapshot.SevenDay?.ResetsAt, zone, now)}";
            text += state.Status switch
            {
                UsageStatus.Ok => "",
                UsageStatus.Error => $" | stale {FormatTime(state.LastUpdated, zone, now)}",
                _ => " | " + StatusText(state),
            };
        }

        return text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];
    }

    public static IReadOnlyList<string> MenuLines(UsageState state, TimeZoneInfo zone, DateTimeOffset now)
    {
        var five = state.Snapshot?.FiveHour;
        var seven = state.Snapshot?.SevenDay;
        var lines = new List<string>
        {
            $"5-hour: {Percent(five)} · resets {FormatTime(five?.ResetsAt, zone, now)}",
            $"7-day: {Percent(seven)} · resets {FormatTime(seven?.ResetsAt, zone, now)}",
        };

        if (state.Status == UsageStatus.Ok)
        {
            lines.Add($"Updated {FormatTime(state.LastUpdated, zone, now)}");
        }
        else
        {
            var status = StatusText(state);
            lines.Add(char.ToUpperInvariant(status[0]) + status[1..]);
            if (state.IsStale)
                lines.Add($"Stale, last updated {FormatTime(state.LastUpdated, zone, now)}");
        }

        return lines;
    }

    /// <summary>Local time as "HH:mm" if it falls on today, otherwise "ddd HH:mm"; "—" if unknown.</summary>
    public static string FormatTime(DateTimeOffset? value, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (value is not { } at)
            return Unknown;

        var local = TimeZoneInfo.ConvertTime(at, zone);
        var format = local.Date == TimeZoneInfo.ConvertTime(now, zone).Date ? "HH:mm" : "ddd HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string StatusText(UsageState state) => state.Status switch
    {
        UsageStatus.Loading => "loading…",
        UsageStatus.TokenExpired => "token expired, open Claude Code",
        UsageStatus.NoCredentials => "no credentials, sign in to Claude Code",
        _ => state.Error ?? "error",
    };

    private static string Percent(UsageWindow? window) =>
        Round(window?.Utilization) is int percent ? $"{percent}%" : Unknown;

    private static int? Round(double? utilization) =>
        utilization is double value ? (int)Math.Round(Math.Max(0, value), MidpointRounding.AwayFromZero) : null;
}
