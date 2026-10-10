using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class JsonSpliceTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Replaces_only_the_value_bytes()
    {
        var json = U("""{"a": 1, "oauthAccount": {"x": "old"} ,"z":[1,2]}""");

        Assert.Equal(U("""{"a": 1, "oauthAccount": {"y":2} ,"z":[1,2]}"""), JsonSplice.Replace(json, U("""{"y":2}"""), "oauthAccount"));
    }

    [Fact]
    public void Span_of_a_string_includes_its_quotes()
    {
        var json = U("""{"claudeAiOauth":{"accessToken":"old","n":5}}""");

        var (start, length) = JsonSplice.Find(json, "claudeAiOauth", "accessToken")!.Value;

        Assert.Equal("\"old\"", Encoding.UTF8.GetString(json, start, length));
    }

    [Fact]
    public void Matches_names_case_sensitively()
    {
        var json = U("""{"projects":{"D:/x":{"n":1},"d:/x":{"n":2}},"OAuthAccount":0,"oauthAccount":1}""");

        Assert.Equal(
            U("""{"projects":{"D:/x":{"n":1},"d:/x":{"n":9}},"OAuthAccount":0,"oauthAccount":1}"""),
            JsonSplice.Replace(json, U("""{"n":9}"""), "projects", "d:/x"));
        Assert.Equal(
            U("""{"projects":{"D:/x":{"n":1},"d:/x":{"n":2}},"OAuthAccount":0,"oauthAccount":7}"""),
            JsonSplice.Replace(json, U("7"), "oauthAccount"));
    }

    [Fact]
    public void Keeps_raw_non_ascii_and_a_missing_trailing_newline()
    {
        var json = U("{\n  \"name\": \"Dimas Ü ✓\",\n  \"n\": 1\n}");

        Assert.Equal(U("{\n  \"name\": \"Dimas Ü ✓\",\n  \"n\": 22\n}"), JsonSplice.Replace(json, U("22"), "n"));
    }

    [Fact]
    public void Keeps_a_trailing_newline()
    {
        Assert.Equal(U("{\"n\": 2}\n"), JsonSplice.Replace(U("{\"n\": 1}\n"), U("2"), "n"));
    }

    [Fact]
    public void Replaces_a_nested_pretty_printed_object()
    {
        var json = U("{\n  \"a\": 1,\n  \"oauthAccount\": {\n    \"accountUuid\": \"x\"\n  },\n  \"b\": 2\n}");
        var value = U("{\n    \"accountUuid\": \"y\",\n    \"emailAddress\": \"t@example.com\"\n  }");

        Assert.Equal(
            U("{\n  \"a\": 1,\n  \"oauthAccount\": {\n    \"accountUuid\": \"y\",\n    \"emailAddress\": \"t@example.com\"\n  },\n  \"b\": 2\n}"),
            JsonSplice.Replace(json, value, "oauthAccount"));
    }

    [Fact]
    public void Leading_bom_is_skipped_and_kept()
    {
        byte[] json = [0xEF, 0xBB, 0xBF, .. U("""{"n":1}""")];

        Assert.Equal([0xEF, 0xBB, 0xBF, .. U("""{"n":2}""")], JsonSplice.Replace(json, U("2"), "n"));
    }

    [Fact]
    public void Missing_path_is_null() => Assert.Null(JsonSplice.Find(U("""{"a":{}}"""), "a", "b"));

    [Fact]
    public void Replace_throws_when_the_path_is_missing() =>
        Assert.Throws<FormatException>(() => JsonSplice.Replace(U("{}"), U("1"), "a"));

    [Fact]
    public void Duplicate_name_on_the_path_is_refused() =>
        Assert.Throws<FormatException>(() => JsonSplice.Find(U("""{"oauthAccount":{},"oauthAccount":{}}"""), "oauthAccount"));

    [Fact]
    public void Non_object_on_the_path_is_refused() =>
        Assert.Throws<FormatException>(() => JsonSplice.Find(U("""{"a":[1]}"""), "a", "b"));

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Invalid_documents_are_refused(string json) =>
        Assert.Throws<FormatException>(() => JsonSplice.Find(U(json), "a"));

    [Fact]
    public void Encoded_values_are_plain_json()
    {
        Assert.Equal("\"sk-ant_x+1\"", Encoding.UTF8.GetString(JsonSplice.EncodeString("sk-ant_x+1")));
        Assert.Equal("1791700000000", Encoding.UTF8.GetString(JsonSplice.EncodeNumber(1791700000000)));
        Assert.Equal("[\"a\",\"b\"]", Encoding.UTF8.GetString(JsonSplice.EncodeStringArray(["a", "b"])));
    }
}
