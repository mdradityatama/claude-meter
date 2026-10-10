using System.Globalization;

namespace ClaudeMeter.Core;

public enum UsageLevel
{
    Unknown,
    Green,
    Yellow,
    Red,
    Error,
}

internal sealed record IconFace(string Text, UsageLevel Level);

/// <param name="Fraction">Progress bar fill, 0–1.</param>
internal sealed record PanelRow(string Label, string Percent, string Resets, double Fraction, UsageLevel Level);

internal sealed record PanelModel(PanelRow FiveHour, PanelRow SevenDay, string Status);

/// <summary>One account as the runtime sees it; formatted into rows by <see cref="DisplayFormatter"/>.</summary>
internal sealed record AccountView(
    AccountKey Key, string Email, string? Plan, bool IsActive, bool IsSaved, UsageState Usage, LoginInfo Login,
    SwitchStage? Switching, bool SwitchedHere, string? CaptureWarning, DateTimeOffset? LoginExpiresAt);

internal enum RowAction
{
    Active,
    Switch,
    Switching,
    LoginExpired,
}

/// <param name="Inferred">The window reset after the data was fetched: shown as ≈0%.</param>
internal sealed record BarModel(string Label, string Percent, string Resets, double Fraction, UsageLevel Level, bool Inferred);

internal sealed record AccountRowModel(
    AccountKey Key, string Email, string? Plan, bool IsActive, RowAction Action,
    BarModel FiveHour, BarModel SevenDay, string Status, Tone StatusTone, string Login, Tone LoginTone);

internal sealed record SettingsRowModel(AccountKey Key, string Email, string Sub, Tone SubTone, bool IsActive);

/// <summary>Turns a <see cref="UsageState"/> into the icon face, hover panel and menu info lines.</summary>
internal static class DisplayFormatter
{
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

    public static PanelModel Panel(UsageState state, TimeZoneInfo zone, DateTimeOffset now)
    {
        var status = state.Status switch
        {
            UsageStatus.Ok => $"Updated {FormatTime(state.LastUpdated, zone, now)}",
            UsageStatus.Error when state.IsStale => $"Stale since {FormatTime(state.LastUpdated, zone, now)} ({StatusText(state)})",
            _ => Capitalize(StatusText(state)),
        };

        return new PanelModel(
            Row("5-hour", state.Snapshot?.FiveHour, zone, now),
            Row("7-day", state.Snapshot?.SevenDay, zone, now),
            status);
    }

    private static PanelRow Row(string label, UsageWindow? window, TimeZoneInfo zone, DateTimeOffset now)
    {
        var resets = "Resets " + FormatTime(window?.ResetsAt, zone, now);
        if (Round(window?.Utilization) is not int percent)
            return new PanelRow(label, Unknown, resets, 0, UsageLevel.Unknown);

        var level = percent >= 100 ? UsageLevel.Red : LevelFor(percent);
        return new PanelRow(label, $"{percent}%", resets, Math.Clamp(window!.Utilization!.Value / 100, 0, 1), level);
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
            lines.Add(Capitalize(StatusText(state)));
            if (state.IsStale)
                lines.Add($"Stale, last updated {FormatTime(state.LastUpdated, zone, now)}");
        }

        return lines;
    }

    public static AccountRowModel AccountRow(AccountView view, TimeZoneInfo zone, DateTimeOffset now)
    {
        var usage = view.Usage;
        var action = view.Switching is not null ? RowAction.Switching
            : view.IsActive ? RowAction.Active
            : view.Login.Expired ? RowAction.LoginExpired
            : RowAction.Switch;

        var (status, tone) = view switch
        {
            { Switching: SwitchStage.WaitingForLock } => ("Waiting for Claude Code's lock…", Tone.Muted),
            { Switching: not null } => ("Switching…", Tone.Muted),
            { Login.Expired: true } => ("Run /login in Claude Code with this account", Tone.Critical),
            { CaptureWarning: { } warning } => (warning, Tone.Caution),
            _ => UsageStatusLine(view, zone, now),
        };

        return new AccountRowModel(
            view.Key, view.Email, PlanBadge(view.Plan), view.IsActive, action,
            Bar("5h", usage.Snapshot?.FiveHour, usage.LastUpdated, zone, now),
            Bar("7d", usage.Snapshot?.SevenDay, usage.LastUpdated, zone, now),
            status, tone,
            view.Login.Expired ? "" : view.Login.Text, view.Login.Tone);
    }

    public static SettingsRowModel SettingsRow(AccountView view, TimeZoneInfo zone, DateTimeOffset now)
    {
        var parts = new List<string>();
        if (PlanBadge(view.Plan) is { } plan)
            parts.Add(plan);
        if (view.IsActive)
            parts.Add("active");

        var date = view.LoginExpiresAt is { } at ? TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM", CultureInfo.InvariantCulture) : null;
        parts.Add(view.Login switch
        {
            { Expired: true } => "login expired",
            { Tone: Tone.Caution } when date is not null => $"login expires {date}",
            _ when date is not null => $"login valid until {date}",
            _ => view.Login.Text.ToLowerInvariant(),
        });

        return new SettingsRowModel(view.Key, view.Email, string.Join(" · ", parts.Where(p => p.Length > 0)), view.Login.Tone, view.IsActive);
    }

    public static string? PlanBadge(string? subscriptionType) => string.IsNullOrEmpty(subscriptionType)
        ? null
        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(subscriptionType.Replace("claude_", "").Replace('_', ' '));

    public static string Age(TimeSpan age) => age switch
    {
        _ when age < TimeSpan.FromMinutes(1) => "just now",
        _ when age < TimeSpan.FromHours(1) => $"{(int)age.TotalMinutes} min ago",
        _ when age < TimeSpan.FromHours(48) => $"{(int)age.TotalHours} h ago",
        _ => $"{(int)age.TotalDays} days ago",
    };

    private static (string, Tone) UsageStatusLine(AccountView view, TimeZoneInfo zone, DateTimeOffset now)
    {
        var usage = view.Usage;
        var age = usage.LastUpdated is { } at ? Age(now - at) : null;
        return usage.Status switch
        {
            UsageStatus.Ok => ($"Live · updated {FormatTime(usage.LastUpdated, zone, now)}", Tone.Muted),
            UsageStatus.Loading => ("Loading…", Tone.Muted),
            UsageStatus.TokenExpired when view.IsActive => view.SwitchedHere
                ? ("Token expired, open Claude Code. If it asks you to log in, run /login with this account", Tone.Caution)
                : ("Token expired, open Claude Code", Tone.Caution),
            UsageStatus.NoCredentials => ("No credentials, sign in to Claude Code", Tone.Caution),
            UsageStatus.NotLive or UsageStatus.TokenExpired => (age is null ? "Not live" : $"Last known {age}", Tone.Muted),
            _ when usage.Snapshot is not null && age is not null => ($"{ErrorLabel(usage.Error)} · last known {age}", Tone.Muted),
            _ => (Capitalize(usage.Error ?? "error"), Tone.Muted),
        };
    }

    private static string ErrorLabel(string? error) =>
        error is "network error" or "timeout" ? "Offline" : Capitalize(error ?? "error");

    /// <summary>A reset that passed after the data was fetched is shown as ≈0% with "reset".</summary>
    private static BarModel Bar(string label, UsageWindow? window, DateTimeOffset? fetchedAt, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (window?.ResetsAt is { } resetsAt && resetsAt <= now && (fetchedAt is null || fetchedAt < resetsAt))
            return new BarModel(label, "≈0%", "reset", 0, UsageLevel.Green, Inferred: true);

        var resets = FormatTime(window?.ResetsAt, zone, now);
        if (Round(window?.Utilization) is not int percent)
            return new BarModel(label, Unknown, resets, 0, UsageLevel.Unknown, false);

        var level = percent >= 100 ? UsageLevel.Red : LevelFor(percent);
        return new BarModel(label, $"{percent}%", resets, Math.Clamp(window!.Utilization!.Value / 100, 0, 1), level, false);
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

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string Percent(UsageWindow? window) =>
        Round(window?.Utilization) is int percent ? $"{percent}%" : Unknown;

    private static int? Round(double? utilization) =>
        utilization is double value ? (int)Math.Round(Math.Max(0, value), MidpointRounding.AwayFromZero) : null;
}
