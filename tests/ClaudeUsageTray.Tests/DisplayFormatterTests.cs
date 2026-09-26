using ClaudeUsageTray.Core;

namespace ClaudeUsageTray.Tests;

public class DisplayFormatterTests
{
    // UTC+7, no DST. 2026-09-29 is a Tuesday.
    private static readonly TimeZoneInfo Local = TimeZoneInfo.CreateCustomTimeZone("test+7", TimeSpan.FromHours(7), "test+7", "test+7");
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 2, 0, 0, TimeSpan.Zero); // Tue 09:00 local

    private static UsageState Ok(double? fiveHour) =>
        new(UsageStatus.Ok, new UsageSnapshot(new UsageWindow(fiveHour, null), null), Now, null);

    [Theory]
    [InlineData(0, UsageLevel.Green)]
    [InlineData(49, UsageLevel.Green)]
    [InlineData(50, UsageLevel.Yellow)]
    [InlineData(79, UsageLevel.Yellow)]
    [InlineData(80, UsageLevel.Red)]
    [InlineData(99, UsageLevel.Red)]
    public void Level_boundaries(int percent, UsageLevel expected)
    {
        Assert.Equal(expected, DisplayFormatter.LevelFor(percent));
    }

    [Theory]
    [InlineData(0.0, "0", UsageLevel.Green)]
    [InlineData(49.4, "49", UsageLevel.Green)]
    [InlineData(49.5, "50", UsageLevel.Yellow)]
    [InlineData(79.4, "79", UsageLevel.Yellow)]
    [InlineData(79.5, "80", UsageLevel.Red)]
    [InlineData(99.4, "99", UsageLevel.Red)]
    [InlineData(99.5, "F", UsageLevel.Red)]
    [InlineData(100.0, "F", UsageLevel.Red)]
    [InlineData(130.0, "F", UsageLevel.Red)]
    public void Icon_uses_rounded_five_hour_value(double utilization, string text, UsageLevel level)
    {
        Assert.Equal(new IconFace(text, level), DisplayFormatter.Icon(Ok(utilization)));
    }

    [Fact]
    public void Icon_is_dash_when_utilization_unknown()
    {
        Assert.Equal(new IconFace("—", UsageLevel.Unknown), DisplayFormatter.Icon(Ok(null)));
        Assert.Equal(new IconFace("—", UsageLevel.Unknown), DisplayFormatter.Icon(new UsageState(UsageStatus.Ok, new UsageSnapshot(null, null), Now, null)));
        Assert.Equal(new IconFace("—", UsageLevel.Unknown), DisplayFormatter.Icon(UsageState.Initial));
    }

    [Theory]
    [InlineData(UsageStatus.TokenExpired)]
    [InlineData(UsageStatus.NoCredentials)]
    public void Icon_is_bang_for_auth_problems_even_with_old_data(UsageStatus status)
    {
        Assert.Equal(new IconFace("!", UsageLevel.Error), DisplayFormatter.Icon(Ok(42) with { Status = status }));
    }

    [Fact]
    public void Icon_is_bang_on_error_without_data_and_keeps_number_when_stale()
    {
        Assert.Equal(new IconFace("!", UsageLevel.Error), DisplayFormatter.Icon(UsageState.Initial with { Status = UsageStatus.Error, Error = "network error" }));
        Assert.Equal(new IconFace("42", UsageLevel.Green), DisplayFormatter.Icon(Ok(42) with { Status = UsageStatus.Error, Error = "network error" }));
    }

    [Fact]
    public void Reset_time_is_local_time_only_today_and_weekday_otherwise()
    {
        Assert.Equal("14:30", DisplayFormatter.FormatTime(new DateTimeOffset(2026, 9, 29, 7, 30, 0, TimeSpan.Zero), Local, Now));
        Assert.Equal("Wed 07:00", DisplayFormatter.FormatTime(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), Local, Now));
        Assert.Equal("—", DisplayFormatter.FormatTime(null, Local, Now));
    }

    [Fact]
    public void Tooltip_matches_spec_format()
    {
        var state = new UsageState(
            UsageStatus.Ok,
            new UsageSnapshot(
                new UsageWindow(42, new DateTimeOffset(2026, 9, 29, 7, 30, 0, TimeSpan.Zero)),
                new UsageWindow(18.2, new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero))),
            Now,
            null);

        Assert.Equal("5h 42% · reset 14:30 | 7d 18% · reset Tue 07:00", DisplayFormatter.Tooltip(state, Local, Now));
    }

    [Fact]
    public void Tooltip_shows_dash_for_missing_values_and_stale_marker()
    {
        var state = new UsageState(UsageStatus.Error, new UsageSnapshot(new UsageWindow(null, null), null), Now.AddMinutes(-30), "network error");

        Assert.Equal("5h — · reset — | 7d — · reset — | stale 08:30", DisplayFormatter.Tooltip(state, Local, Now));
    }

    [Theory]
    [InlineData(UsageStatus.Loading, null, "Claude usage: loading…")]
    [InlineData(UsageStatus.TokenExpired, "token expired", "Claude usage: token expired, open Claude Code")]
    [InlineData(UsageStatus.NoCredentials, "no credentials", "Claude usage: no credentials, sign in to Claude Code")]
    [InlineData(UsageStatus.Error, "network error", "Claude usage: network error")]
    public void Tooltip_without_data_shows_status(UsageStatus status, string? error, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.Tooltip(new UsageState(status, null, null, error), Local, Now));
    }

    [Fact]
    public void Tooltip_never_exceeds_127_chars()
    {
        var state = new UsageState(UsageStatus.Error, null, null, new string('x', 300));

        Assert.Equal(127, DisplayFormatter.Tooltip(state, Local, Now).Length);
    }

    [Fact]
    public void Menu_lines_for_ok_state()
    {
        var state = new UsageState(
            UsageStatus.Ok,
            new UsageSnapshot(
                new UsageWindow(42, new DateTimeOffset(2026, 9, 29, 7, 30, 0, TimeSpan.Zero)),
                new UsageWindow(18, null)),
            Now,
            null);

        Assert.Equal(
            ["5-hour: 42% · resets 14:30", "7-day: 18% · resets —", "Updated 09:00"],
            DisplayFormatter.MenuLines(state, Local, Now));
    }

    [Fact]
    public void Menu_lines_for_token_expired_with_old_data()
    {
        var state = new UsageState(UsageStatus.TokenExpired, new UsageSnapshot(null, null), Now.AddMinutes(-15), "token expired");

        Assert.Equal(
            ["5-hour: — · resets —", "7-day: — · resets —", "Token expired, open Claude Code", "Stale, last updated 08:45"],
            DisplayFormatter.MenuLines(state, Local, Now));
    }
}
