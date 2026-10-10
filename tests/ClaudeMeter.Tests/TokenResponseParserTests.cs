using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class TokenResponseParserTests
{
    [Fact]
    public void Full_success_response()
    {
        var result = Assert.IsType<TokenResult.Success>(TokenResponseParser.ParseSuccess(
            """{"access_token":"a2","refresh_token":"r2","expires_in":28800,"scope":"user:inference user:profile"}"""));

        Assert.Equal("a2", result.AccessToken);
        Assert.Equal(TimeSpan.FromHours(8), result.ExpiresIn);
        Assert.Equal("r2", result.RefreshToken);
        Assert.Equal(["user:inference", "user:profile"], result.Scopes);
    }

    [Fact]
    public void Refresh_token_and_scope_are_optional()
    {
        var result = Assert.IsType<TokenResult.Success>(TokenResponseParser.ParseSuccess("""{"access_token":"a2","expires_in":60}"""));

        Assert.Null(result.RefreshToken);
        Assert.Null(result.Scopes);
    }

    [Fact]
    public void Scope_may_be_an_array()
    {
        var result = Assert.IsType<TokenResult.Success>(TokenResponseParser.ParseSuccess(
            """{"access_token":"a2","expires_in":60,"scope":["user:inference","user:profile"]}"""));

        Assert.Equal(["user:inference", "user:profile"], result.Scopes);
    }

    [Theory]
    [InlineData("""{"expires_in":60}""")]
    [InlineData("""{"access_token":"a2"}""")]
    [InlineData("""{"access_token":"a2","expires_in":0}""")]
    [InlineData("not json")]
    public void Invalid_success_bodies_are_transient(string json)
    {
        Assert.Equal(new TokenResult.Transient(null, "invalid response"), TokenResponseParser.ParseSuccess(json));
    }

    [Fact]
    public void Error_bodies_map_to_their_meaning()
    {
        Assert.Equal(new TokenResult.InvalidGrant(), TokenResponseParser.ParseError("""{"error":"invalid_grant"}"""));
        Assert.Equal(new TokenResult.InvalidClient(), TokenResponseParser.ParseError("""{"error":"invalid_client"}"""));
        Assert.Equal(new TokenResult.Transient(null, "HTTP 400"), TokenResponseParser.ParseError("""{"error":"invalid_request"}"""));
        Assert.Equal(new TokenResult.Transient(null, "HTTP 400"), TokenResponseParser.ParseError("not json"));
    }
}
