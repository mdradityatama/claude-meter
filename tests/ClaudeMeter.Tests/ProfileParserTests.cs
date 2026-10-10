using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class ProfileParserTests
{
    [Fact]
    public void Reads_account_and_organization()
    {
        Assert.Equal(
            new ProfileResult.Found("a", "o"),
            ProfileParser.Parse("""{"account":{"uuid":"a","email":"x"},"organization":{"uuid":"o"},"application":{}}"""));
    }

    [Fact]
    public void Organization_is_optional()
    {
        Assert.Equal(new ProfileResult.Found("a", null), ProfileParser.Parse("""{"account":{"uuid":"a"}}"""));
    }

    [Theory]
    [InlineData("""{"account":{"email":"x"}}""")]
    [InlineData("""{"account":null}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Unexpected_shapes_are_unavailable(string json)
    {
        Assert.IsType<ProfileResult.Unavailable>(ProfileParser.Parse(json));
    }
}
