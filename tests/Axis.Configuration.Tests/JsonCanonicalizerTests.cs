using System.Text.Json;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Tests;

public sealed class JsonCanonicalizerTests
{
    [Fact]
    public void Object_properties_are_sorted_by_utf16_code_units_and_whitespace_is_removed()
    {
        var canonical = Canonicalize("""
            {
              "b": [ 1, { "z": true, "y": null } ],
              "a": "x",
              "A": false
            }
            """);

        Assert.Equal("""{"A":false,"a":"x","b":[1,{"y":null,"z":true}]}""", canonical);
    }

    [Fact]
    public void Property_order_follows_utf16_code_units_rather_than_code_points()
    {
        // The example from RFC 8785, section 3.2.3: a surrogate pair sorts before U+FB33.
        var canonical = Canonicalize("""
            { "\u20ac": 1, "\r": 2, "\ufb33": 3, "1": 4, "\ud83d\ude00": 5, "\u0080": 6, "\u00f6": 7 }
            """);

        Assert.Equal("{\"\\r\":2,\"1\":4,\"\u0080\":6,\"\u00f6\":7,\"\u20ac\":1,\"\ud83d\ude00\":5,\"\ufb33\":3}", canonical);
    }

    [Fact]
    public void Strings_escape_only_quotes_backslashes_and_control_characters()
    {
        var canonical = Canonicalize("""
            "\" \\ \/ \b \f \n \r \t \u0001 \u001F \u00e9 \u20ac caf\u00e9 😀"
            """);

        Assert.Equal("\"\\\" \\\\ / \\b \\f \\n \\r \\t \\u0001 \\u001f é € café 😀\"", canonical);
    }

    [Theory]
    [InlineData("2.0", "2")]
    [InlineData("-0", "0")]
    [InlineData("4.50", "4.5")]
    [InlineData("2e-3", "0.002")]
    [InlineData("1e-6", "0.000001")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("123.456e2", "12345.6")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("1e21", "1e+21")]
    [InlineData("1E30", "1e+30")]
    [InlineData("-1.5e-30", "-1.5e-30")]
    [InlineData("0.000000000000000000000000001", "1e-27")]
    [InlineData("333333333.33333329", "333333333.3333333")]
    [InlineData("9007199254740993", "9007199254740992")]
    public void Numbers_are_written_as_ecmascript_does(string json, string expected) =>
        Assert.Equal(expected, Canonicalize(json));

    [Fact]
    public void Number_outside_the_double_range_is_rejected() =>
        Assert.Throws<JsonException>(() => Canonicalize("1e400"));

    private static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonCanonicalizer.Canonicalize(document.RootElement);
    }
}
