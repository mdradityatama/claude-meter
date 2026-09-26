using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class UsageParserTests
{
    [Fact]
    public void Parses_full_real_response()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "usage-response.json"));

        var snapshot = UsageParser.Parse(json);

        Assert.Equal(28.0, snapshot.FiveHour?.Utilization);
        Assert.Equal(DateTimeOffset.Parse("2026-09-26T17:20:00.089635+00:00"), snapshot.FiveHour?.ResetsAt);
        Assert.Equal(31.0, snapshot.SevenDay?.Utilization);
        Assert.Equal(DateTimeOffset.Parse("2026-09-27T10:00:00.089664+00:00"), snapshot.SevenDay?.ResetsAt);
    }

    [Fact]
    public void Missing_windows_are_null()
    {
        var snapshot = UsageParser.Parse("{}");

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.SevenDay);
    }

    [Fact]
    public void Null_windows_are_null()
    {
        var snapshot = UsageParser.Parse("""{"five_hour":null,"seven_day":null}""");

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.SevenDay);
    }

    [Fact]
    public void Null_and_missing_fields_inside_window_are_null()
    {
        var snapshot = UsageParser.Parse("""{"five_hour":{"utilization":null,"resets_at":null},"seven_day":{}}""");

        Assert.Null(snapshot.FiveHour?.Utilization);
        Assert.Null(snapshot.FiveHour?.ResetsAt);
        Assert.Null(snapshot.SevenDay?.Utilization);
        Assert.Null(snapshot.SevenDay?.ResetsAt);
    }

    [Fact]
    public void Unexpected_extra_fields_are_ignored()
    {
        var snapshot = UsageParser.Parse("""{"new_thing":[1,2],"five_hour":{"utilization":5,"resets_at":"2026-01-01T00:00:00Z","extra":{"a":1}}}""");

        Assert.Equal(5.0, snapshot.FiveHour?.Utilization);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), snapshot.FiveHour?.ResetsAt);
    }

    [Fact]
    public void Wrong_types_are_treated_as_missing()
    {
        var snapshot = UsageParser.Parse("""{"five_hour":{"utilization":"high","resets_at":"not a date"},"seven_day":42}""");

        Assert.Null(snapshot.FiveHour?.Utilization);
        Assert.Null(snapshot.FiveHour?.ResetsAt);
        Assert.Null(snapshot.SevenDay);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Invalid_document_throws_format_exception(string json)
    {
        Assert.Throws<FormatException>(() => UsageParser.Parse(json));
    }
}
