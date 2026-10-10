using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>
/// Replaces one property value in a JSON document without re-serializing it: every byte outside the
/// value's span stays identical. Names match case-sensitively; a duplicate target name is refused,
/// because JavaScript's JSON.parse would use the last one and the edit could hit the wrong copy.
/// </summary>
internal static class JsonSplice
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <returns>Start and length of the value (strings include their quotes), or null if absent.</returns>
    /// <exception cref="FormatException">Invalid JSON, a non-object on the path, or a duplicate name.</exception>
    public static (int Start, int Length)? Find(ReadOnlySpan<byte> json, params string[] path)
    {
        var offset = json.StartsWith(Bom) ? Bom.Length : 0;
        try
        {
            var reader = new Utf8JsonReader(json[offset..]);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new FormatException("JSON root is not an object.");

            (int Start, int Length) span = default;
            var atValue = reader;
            for (var i = 0; i < path.Length; i++)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new FormatException($"'{path[i - 1]}' is not an object.");
                if (!FindMember(ref reader, path[i], ref atValue, out span))
                    return null;
                reader = atValue;
            }

            return (span.Start + offset, span.Length);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Invalid JSON.", ex);
        }
    }

    /// <exception cref="FormatException">As <see cref="Find"/>, or the path is absent.</exception>
    public static byte[] Replace(ReadOnlySpan<byte> json, ReadOnlySpan<byte> newValue, params string[] path)
    {
        var (start, length) = Find(json, path) ?? throw new FormatException($"'{string.Join('.', path)}' not found.");
        return [.. json[..start], .. newValue, .. json[(start + length)..]];
    }

    public static byte[] EncodeString(string value) => JsonSerializer.SerializeToUtf8Bytes(value, Relaxed);

    public static byte[] EncodeNumber(long value) => Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));

    public static byte[] EncodeStringArray(IReadOnlyList<string> values) => JsonSerializer.SerializeToUtf8Bytes(values, Relaxed);

    /// <summary>Scans every member of the object at <paramref name="reader"/> so a duplicate is noticed.</summary>
    /// <param name="atValue">Set to a reader positioned at the matching value.</param>
    private static bool FindMember(ref Utf8JsonReader reader, string name, ref Utf8JsonReader atValue, out (int Start, int Length) span)
    {
        var found = false;
        span = default;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var matches = reader.ValueTextEquals(name);
            reader.Read();
            var valueReader = reader;
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            if (!matches)
                continue;
            if (found)
                throw new FormatException($"Duplicate property '{name}'.");
            found = true;
            atValue = valueReader;
            span = (start, (int)reader.BytesConsumed - start);
        }

        return found;
    }
}
