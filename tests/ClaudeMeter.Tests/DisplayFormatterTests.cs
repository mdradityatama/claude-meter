using System.Globalization;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

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

    [Theory]
    [InlineData(0, "<1m")]
    [InlineData(59, "<1m")]
    [InlineData(60, "1m")]
    [InlineData(59 * 60 + 59, "59m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(4 * 3600 + 12 * 60 + 40, "4h 12m")]
    [InlineData(23 * 3600 + 59 * 60, "23h 59m")]
    [InlineData(24 * 3600, "1d 0h")]
    [InlineData(6 * 86400 + 23 * 3600 + 59 * 60, "6d 23h")]
    public void Countdown_shows_the_two_largest_units_rounded_down(int seconds, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.Countdown(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Reset_date_is_local_time_only_today_and_day_month_otherwise()
    {
        var other = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("14:30", DisplayFormatter.ResetDate(new DateTimeOffset(2026, 9, 29, 7, 30, 0, TimeSpan.Zero), Local, Now, CultureInfo.InvariantCulture));
        Assert.Equal("6 Oct 07:00", DisplayFormatter.ResetDate(other, Local, Now, CultureInfo.InvariantCulture));
        Assert.Equal("—", DisplayFormatter.ResetDate(null, Local, Now, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Reset_date_month_follows_the_culture_but_time_stays_24_hour()
    {
        var other = new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);
        Assert.Equal("6 Okt 13:00", DisplayFormatter.ResetDate(other, Local, Now, CultureInfo.GetCultureInfo("id-ID")));
        Assert.Equal("6 Oct 13:00", DisplayFormatter.ResetDate(other, Local, Now, CultureInfo.GetCultureInfo("en-US")));
    }
}
