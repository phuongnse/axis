using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

public sealed class RunnerValuesTests
{
    private static readonly PackageField[] _fields =
    [
        new("text", FieldType.Text, false),
        new("integer", FieldType.Integer, false),
        new("decimal", FieldType.Decimal, false),
        new("boolean", FieldType.Boolean, false),
        new("date", FieldType.Date, false),
        new("dateTime", FieldType.DateTime, false),
    ];

    [Fact]
    public void Each_field_type_round_trips_through_the_codec()
    {
        var values = new Dictionary<string, object?>
        {
            ["text"] = "a \"quoted\" line",
            ["integer"] = -42L,
            ["decimal"] = 12.50m,
            ["boolean"] = true,
            ["date"] = new DateOnly(2025, 3, 9),
            ["dateTime"] = new DateTimeOffset(2025, 3, 9, 14, 30, 5, TimeSpan.FromHours(7)),
        };

        var decoded = RunnerValues.Decode(_fields, RunnerValues.Encode(_fields, values));

        Assert.Equal(values, decoded);
    }

    [Fact]
    public void A_large_integer_and_a_28_digit_decimal_are_exact_strings()
    {
        var values = new Dictionary<string, object?>
        {
            ["integer"] = 9007199254740993L,
            ["decimal"] = 1234567890123456789012345.678m,
        };

        var encoded = RunnerValues.Encode(_fields, values);

        Assert.Equal("9007199254740993", encoded["integer"]!.GetValue<string>());
        Assert.Equal("1234567890123456789012345.678", encoded["decimal"]!.GetValue<string>());
        Assert.Equal(values["integer"], RunnerValues.Decode(_fields, encoded)["integer"]);
        Assert.Equal(values["decimal"], RunnerValues.Decode(_fields, encoded)["decimal"]);
    }

    [Fact]
    public void The_encoded_values_use_the_formats_of_the_protocol()
    {
        var encoded = RunnerValues.Encode(
            _fields,
            new Dictionary<string, object?>
            {
                ["boolean"] = false,
                ["date"] = new DateOnly(2025, 3, 9),
                ["dateTime"] = new DateTimeOffset(2025, 3, 9, 14, 30, 5, TimeSpan.Zero),
            });

        Assert.Equal(JsonValueKind.False, encoded["boolean"]!.GetValueKind());
        Assert.Equal("2025-03-09", encoded["date"]!.GetValue<string>());
        Assert.Equal("2025-03-09T14:30:05.0000000+00:00", encoded["dateTime"]!.GetValue<string>());
    }

    [Fact]
    public void A_missing_field_decodes_to_null_and_a_null_value_encodes_to_null()
    {
        var decoded = RunnerValues.Decode(_fields, new JsonObject { ["integer"] = "7", ["text"] = null });

        Assert.Equal(7L, decoded["integer"]);
        Assert.All(_fields.Where(field => field.Name != "integer"), field => Assert.Null(decoded[field.Name]));

        var encoded = RunnerValues.Encode(_fields, new Dictionary<string, object?> { ["text"] = null });
        Assert.True(encoded.ContainsKey("text"));
        Assert.Null(encoded["text"]);
        Assert.False(encoded.ContainsKey("integer"));
    }

    [Fact]
    public void A_value_that_is_not_in_a_field_of_the_manifest_is_not_encoded()
    {
        var encoded = RunnerValues.Encode(_fields, new Dictionary<string, object?> { ["text"] = "a", ["extra"] = "b" });

        Assert.Equal(["text"], encoded.Select(pair => pair.Key));
    }

    [Theory]
    [InlineData("integer", "12.5")]
    [InlineData("integer", "9223372036854775808")]
    [InlineData("integer", "+")]
    [InlineData("decimal", "1e3")]
    [InlineData("decimal", "abc")]
    [InlineData("date", "09/03/2025")]
    [InlineData("date", "2025-03-09T00:00:00Z")]
    [InlineData("dateTime", "2025-03-09T14:30:05")]
    [InlineData("dateTime", "2025-03-09")]
    [InlineData("dateTime", "not a date")]
    public void A_value_in_the_wrong_format_throws_a_format_exception(string field, string value)
    {
        var exception = Assert.Throws<FormatException>(() => RunnerValues.Decode(_fields, new JsonObject { [field] = value }));

        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("integer", 7)]
    [InlineData("boolean", "true")]
    [InlineData("text", true)]
    public void A_value_of_the_wrong_json_kind_throws_a_format_exception(string field, object value)
    {
        var node = value switch
        {
            int number => JsonValue.Create(number),
            string text => JsonValue.Create(text),
            _ => JsonValue.Create((bool)value),
        };

        Assert.Throws<FormatException>(() => RunnerValues.Decode(_fields, new JsonObject { [field] = node }));
    }

    [Theory]
    [InlineData("text", 5)]
    [InlineData("integer", 5)]
    [InlineData("integer", "5")]
    [InlineData("decimal", 5.5)]
    [InlineData("boolean", "true")]
    [InlineData("date", "2025-03-09")]
    [InlineData("dateTime", "2025-03-09T14:30:05+00:00")]
    public void A_value_of_the_wrong_net_type_throws_an_invalid_cast_exception(string field, object value) =>
        Assert.Throws<InvalidCastException>(() => RunnerValues.Encode(_fields, new Dictionary<string, object?> { [field] = value }));
}
