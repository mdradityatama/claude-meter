using System.Globalization;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AccountRowTests
{
    // UTC+7, no DST. 2026-09-29 is a Tuesday.
    private static readonly TimeZoneInfo Local = TimeZoneInfo.CreateCustomTimeZone("test+7", TimeSpan.FromHours(7), "test+7", "test+7");
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 2, 0, 0, TimeSpan.Zero); // Tue 09:00 local
    private static readonly AccountKey Key = new("acc-a", "org-a");
    private static readonly LoginInfo Valid = new("Login valid 13 days", Tone.Muted, false);

    private static readonly UsageSnapshot Snapshot = new(
        new UsageWindow(42, new DateTimeOffset(2026, 9, 29, 7, 30, 0, TimeSpan.Zero)),
        new UsageWindow(81.6, new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero)));

    private static AccountView View(
        UsageState? usage = null, bool active = false, bool saved = true, LoginInfo? login = null, SwitchStage? switching = null,
        bool switchedHere = false, string? warning = null, string? plan = "pro", DateTimeOffset? loginExpiresAt = null) =>
        new(Key, "a@example.com", plan, active, saved, usage ?? new UsageState(UsageStatus.Ok, Snapshot, Now, null),
            login ?? Valid, switching, switchedHere, warning, loginExpiresAt);

    private static AccountRowModel Row(AccountView view) => DisplayFormatter.AccountRow(view, Local, Now, CultureInfo.InvariantCulture);

    [Fact]
    public void Active_live_row()
    {
        var row = Row(View(active: true));

        Assert.Equal(RowAction.Active, row.Action);
        Assert.Equal(("Live · updated 09:00", Tone.Muted), (row.Status, row.StatusTone));
        Assert.Equal(new BarModel("5h", "42%", "5h 30m", "14:30", 0.42, UsageLevel.Green, false), row.FiveHour);
        Assert.Equal(new BarModel("7d", "82%", "6d 22h", "6 Oct 07:00", 0.816, UsageLevel.Red, false), row.SevenDay);
        Assert.Equal(("Login valid 13 days", Tone.Muted), (row.Login, row.LoginTone));
        Assert.Equal("Pro", row.Plan);
    }

    [Fact]
    public void Inactive_row_offers_switch()
    {
        Assert.Equal(RowAction.Switch, Row(View()).Action);
    }

    [Fact]
    public void Inactive_offline_row_shows_age()
    {
        var old = Now.AddHours(-2);

        Assert.Equal("Offline · last known 2 h ago", Row(View(new UsageState(UsageStatus.Error, Snapshot, old, "network error"))).Status);
        Assert.Equal("Rate limited · last known 2 h ago", Row(View(new UsageState(UsageStatus.Error, Snapshot, old, "rate limited"))).Status);
        Assert.Equal("Network error", Row(View(new UsageState(UsageStatus.Error, null, null, "network error"))).Status);
    }

    [Fact]
    public void Not_live_row_shows_last_known_age()
    {
        Assert.Equal("Last known 3 days ago", Row(View(new UsageState(UsageStatus.NotLive, Snapshot, Now.AddDays(-3), null))).Status);
        Assert.Equal("Not live", Row(View(new UsageState(UsageStatus.NotLive, null, null, null))).Status);
    }

    [Fact]
    public void Reset_inference_at_the_boundary()
    {
        UsageState FetchedAt(DateTimeOffset fetched, DateTimeOffset resetsAt) =>
            new(UsageStatus.Ok, new UsageSnapshot(new UsageWindow(42, resetsAt), null), fetched, null);

        Assert.Equal(new BarModel("5h", "≈0%", "reset", "reset", 0, UsageLevel.Green, true), Row(View(FetchedAt(Now.AddMinutes(-9), Now))).FiveHour);
        Assert.False(Row(View(FetchedAt(Now.AddMinutes(-9), Now.AddSeconds(1)))).FiveHour.Inferred);
        Assert.False(Row(View(FetchedAt(Now, Now))).FiveHour.Inferred);
    }

    [Fact]
    public void Bar_is_clamped_to_full()
    {
        var usage = new UsageState(UsageStatus.Ok, new UsageSnapshot(new UsageWindow(130, null), null), Now, null);

        Assert.Equal(new BarModel("5h", "130%", "—", "—", 1, UsageLevel.Red, false), Row(View(usage)).FiveHour);
    }

    [Fact]
    public void Unknown_usage_shows_dashes()
    {
        Assert.Equal(new BarModel("7d", "—", "—", "—", 0, UsageLevel.Unknown, false), Row(View(UsageState.Initial)).SevenDay);
    }

    [Fact]
    public void Expired_login_row()
    {
        var row = Row(View(login: new LoginInfo("Login expired", Tone.Critical, true)));

        Assert.Equal(RowAction.LoginExpired, row.Action);
        Assert.Equal(("Run /login in Claude Code with this account", Tone.Critical), (row.Status, row.StatusTone));
        Assert.Equal("", row.Login);
    }

    [Fact]
    public void Switching_row()
    {
        var waiting = Row(View(switching: SwitchStage.WaitingForLock));

        Assert.Equal(RowAction.Switching, waiting.Action);
        Assert.Equal("Waiting for Claude Code's lock…", waiting.Status);
        Assert.Equal("Switching…", Row(View(switching: SwitchStage.Writing)).Status);
    }

    [Fact]
    public void Unsaved_active_row()
    {
        var row = Row(View(active: true, saved: false, warning: "Not saved yet: waiting for network to verify"));

        Assert.Equal(("Not saved yet: waiting for network to verify", Tone.Caution), (row.Status, row.StatusTone));
    }

    [Fact]
    public void Token_expired_after_a_switch()
    {
        var expired = new UsageState(UsageStatus.TokenExpired, Snapshot, Now, "token expired");

        Assert.Equal(
            ("Token expired, open Claude Code. If it asks you to log in, run /login with this account", Tone.Caution),
            (Row(View(expired, active: true, switchedHere: true)).Status, Row(View(expired, active: true, switchedHere: true)).StatusTone));
        Assert.Equal("Token expired, open Claude Code", Row(View(expired, active: true)).Status);
        Assert.Equal("Last known just now", Row(View(expired)).Status);
    }

    [Theory]
    [InlineData("pro", "Pro")]
    [InlineData("max", "Max")]
    [InlineData("claude_team", "Team")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Plan_badge(string? subscription, string? badge)
    {
        Assert.Equal(badge, DisplayFormatter.PlanBadge(subscription));
    }

    [Fact]
    public void Settings_row_sub_line()
    {
        var at = new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero);
        var soon = new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero);

        var active = DisplayFormatter.SettingsRow(View(active: true, loginExpiresAt: at), Local, Now);
        var caution = DisplayFormatter.SettingsRow(
            View(plan: "max", login: new LoginInfo("Login expires in 2 days", Tone.Caution, false), loginExpiresAt: soon), Local, Now);
        var expired = DisplayFormatter.SettingsRow(View(login: new LoginInfo("Login expired", Tone.Critical, true)), Local, Now);

        Assert.Equal(("Pro · active · login valid until 24 Oct", Tone.Muted), (active.Sub, active.SubTone));
        Assert.Equal(("Max · login expires 1 Oct", Tone.Caution), (caution.Sub, caution.SubTone));
        Assert.Equal(("Pro · login expired", Tone.Critical), (expired.Sub, expired.SubTone));
    }

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(15 * 60, "15 min ago")]
    [InlineData(2 * 3600, "2 h ago")]
    [InlineData(47 * 3600, "47 h ago")]
    [InlineData(3 * 86400, "3 days ago")]
    public void Age_format(int seconds, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.Age(TimeSpan.FromSeconds(seconds)));
    }
}
