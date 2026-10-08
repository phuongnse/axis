using System.Globalization;
using System.Text;
using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordInputParserTests
{
    private static readonly EntityModel _target = Entity(
        Guid.Parse("7c2e4f10-3b1a-4d5e-8f60-1a2b3c4d5e01"),
        "Department",
        "entities/department.json",
        Field("name", FieldType.Text));

    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01"),
        "Order",
        "entities/order.json",
        Field("title", FieldType.Text, required: true, maxLength: 5),
        Field("count", FieldType.Integer),
        Field("total", FieldType.Decimal, precision: 5, scale: 2),
        Field("amount", FieldType.Decimal),
        Field("active", FieldType.Boolean),
        Field("day", FieldType.Date),
        Field("at", FieldType.DateTime),
        Field("status", FieldType.Enum, values: ["Open", "Closed"]),
        Field("dept", FieldType.Reference, target: _target),
        Field("lines", FieldType.ChildCollection, target: _target));

    private static readonly string[] _forbidden =
    [
        "entities", "e_", "f_", "uq_", "fk_", "bigint", "numeric", "character varying", "timestamp", "Exception",
    ];

    /// <summary>
    /// Each row is a body with exactly one problem: the JSON, the operation, the expected pointer
    /// and the offending text from the body that no message may repeat (null when there is none).
    /// </summary>
    public static TheoryData<string, RecordOperation, string, string?> ErrorCases => new()
    {
        // The body itself.
        { """{ "values": """, RecordOperation.Create, "", null },
        { "", RecordOperation.Create, "", null },
        { "[]", RecordOperation.Create, "", null },
        { "\"values\"", RecordOperation.Create, "", "values" },
        { new string('[', 100) + new string(']', 100), RecordOperation.Create, "", null },
        { """{ "values": { "\ud800": 1 } }""", RecordOperation.Create, "", @"\ud800" },
        { """{ "\udc00": 1, "values": { "title": "Hi" } }""", RecordOperation.Create, "", @"\udc00" },

        // Body properties.
        { """{ "values": { "title": "Hi" }, "colour": 1 }""", RecordOperation.Create, "/colour", "colour" },
        { """{ "values": { "title": "Hi" }, "version": 1 }""", RecordOperation.Create, "/version", "version" },
        { """{ "values": { "title": "Hi" }, "a/b~c": 1 }""", RecordOperation.Create, "/a~1b~0c", "a/b~c" },
        { """{ "values": { "title": "Hi" }, "values": { "title": "Hi" } }""", RecordOperation.Create, "/values", "values" },
        { "{}", RecordOperation.Create, "/values", null },
        { """{ "version": 1 }""", RecordOperation.Update, "/values", null },
        { """{ "values": [] }""", RecordOperation.Create, "/values", null },
        { """{ "values": null }""", RecordOperation.Create, "/values", null },
        { """{ "values": { "title": "Hi" }, "version": 1, "version": 2 }""", RecordOperation.Update, "/version", "version" },

        // Value property names.
        { """{ "values": { "title": "Hi", "colour": 1 } }""", RecordOperation.Create, "/values/colour", "colour" },
        { """{ "values": { "title": "Hi", "Count": 1 } }""", RecordOperation.Create, "/values/Count", "Count" },
        { """{ "values": { "title": "Hi", "a/b~c": 1 } }""", RecordOperation.Create, "/values/a~1b~0c", "a/b~c" },
        { """{ "values": { "title": "Hi", "count": 1, "count": 2 } }""", RecordOperation.Create, "/values/count", "count" },
        { """{ "values": { "title": "Hi", "lines": [] } }""", RecordOperation.Create, "/values/lines", "lines" },

        // Required fields.
        { """{ "values": {} }""", RecordOperation.Create, "/values/title", null },
        { """{ "values": { "count": 1 } }""", RecordOperation.Create, "/values/title", null },
        { """{ "values": { "title": null } }""", RecordOperation.Create, "/values/title", null },
        { """{ "values": { "title": null }, "version": 1 }""", RecordOperation.Update, "/values/title", null },

        // A wrong JSON type for each field type.
        { """{ "values": { "title": 5 } }""", RecordOperation.Create, "/values/title", null },
        { """{ "values": { "title": "Hi", "count": "5" } }""", RecordOperation.Create, "/values/count", null },
        { """{ "values": { "title": "Hi", "total": "1.5" } }""", RecordOperation.Create, "/values/total", "1.5" },
        { """{ "values": { "title": "Hi", "amount": true } }""", RecordOperation.Create, "/values/amount", "true" },
        { """{ "values": { "title": "Hi", "active": 1 } }""", RecordOperation.Create, "/values/active", null },
        { """{ "values": { "title": "Hi", "day": 20260101 } }""", RecordOperation.Create, "/values/day", "20260101" },
        { """{ "values": { "title": "Hi", "at": true } }""", RecordOperation.Create, "/values/at", "true" },
        { """{ "values": { "title": "Hi", "status": 1 } }""", RecordOperation.Create, "/values/status", null },
        { """{ "values": { "title": "Hi", "dept": {} } }""", RecordOperation.Create, "/values/dept", null },

        // text
        { """{ "values": { "title": "abcdef" } }""", RecordOperation.Create, "/values/title", "abcdef" },
        { """{ "values": { "title": "😀😀😀😀😀😀" } }""", RecordOperation.Create, "/values/title", "😀😀😀😀😀😀" },
        { """{ "values": { "title": "a\u0000b" } }""", RecordOperation.Create, "/values/title", "a\0b" },
        { """{ "values": { "title": "a\ud800" } }""", RecordOperation.Create, "/values/title", @"a\ud800" },

        // integer
        { """{ "values": { "title": "Hi", "count": 5.5 } }""", RecordOperation.Create, "/values/count", "5.5" },
        { """{ "values": { "title": "Hi", "count": 9223372036854775808 } }""", RecordOperation.Create, "/values/count", "9223372036854775808" },
        { """{ "values": { "title": "Hi", "count": -9223372036854775809 } }""", RecordOperation.Create, "/values/count", "-9223372036854775809" },
        { """{ "values": { "title": "Hi", "count": 1e1000000 } }""", RecordOperation.Create, "/values/count", "1e1000000" },
        { """{ "values": { "title": "Hi", "count": 1e9223372036854775807 } }""", RecordOperation.Create, "/values/count", "1e9223372036854775807" },
        { """{ "values": { "title": "Hi", "count": 1e-1 } }""", RecordOperation.Create, "/values/count", "1e-1" },

        // decimal with precision 5 and scale 2
        { """{ "values": { "title": "Hi", "total": 1.234 } }""", RecordOperation.Create, "/values/total", "1.234" },
        { """{ "values": { "title": "Hi", "total": 1234 } }""", RecordOperation.Create, "/values/total", "1234" },
        { """{ "values": { "title": "Hi", "total": 0.12345e2 } }""", RecordOperation.Create, "/values/total", "0.12345e2" },
        { """{ "values": { "title": "Hi", "total": 1e1000000 } }""", RecordOperation.Create, "/values/total", "1e1000000" },

        // decimal without precision
        { """{ "values": { "title": "Hi", "amount": 1e-16384 } }""", RecordOperation.Create, "/values/amount", "1e-16384" },
        { """{ "values": { "title": "Hi", "amount": 1e131072 } }""", RecordOperation.Create, "/values/amount", "1e131072" },
        { """{ "values": { "title": "Hi", "amount": 1e1000000 } }""", RecordOperation.Create, "/values/amount", "1e1000000" },
        { """{ "values": { "title": "Hi", "amount": 1e99999999999999999999 } }""", RecordOperation.Create, "/values/amount", "1e99999999999999999999" },
        { """{ "values": { "title": "Hi", "amount": 1e-99999999999999999999 } }""", RecordOperation.Create, "/values/amount", "1e-99999999999999999999" },

        // date
        { """{ "values": { "title": "Hi", "day": "2026-02-30" } }""", RecordOperation.Create, "/values/day", "2026-02-30" },
        { """{ "values": { "title": "Hi", "day": "2026-2-03" } }""", RecordOperation.Create, "/values/day", "2026-2-03" },
        { """{ "values": { "title": "Hi", "day": "0000-01-01" } }""", RecordOperation.Create, "/values/day", "0000-01-01" },
        { """{ "values": { "title": "Hi", "day": "2026-01-01\n" } }""", RecordOperation.Create, "/values/day", "2026-01-01" },

        // date-time
        { """{ "values": { "title": "Hi", "at": "2026-10-06T08:30:00" } }""", RecordOperation.Create, "/values/at", "2026-10-06T08:30:00" },
        { """{ "values": { "title": "Hi", "at": "2026-10-06T08:30:00.1234567Z" } }""", RecordOperation.Create, "/values/at", "2026-10-06T08:30:00.1234567Z" },
        { """{ "values": { "title": "Hi", "at": "0001-01-01T00:30:00+01:00" } }""", RecordOperation.Create, "/values/at", "0001-01-01T00:30:00+01:00" },
        { """{ "values": { "title": "Hi", "at": "9999-12-31T23:30:00-01:00" } }""", RecordOperation.Create, "/values/at", "9999-12-31T23:30:00-01:00" },
        { """{ "values": { "title": "Hi", "at": "2026-12-31T23:59:60Z" } }""", RecordOperation.Create, "/values/at", "2026-12-31T23:59:60Z" },
        { """{ "values": { "title": "Hi", "at": "2026-10-06T24:00:00Z" } }""", RecordOperation.Create, "/values/at", "2026-10-06T24:00:00Z" },
        { """{ "values": { "title": "Hi", "at": "2026-10-06T08:30:00+14:01" } }""", RecordOperation.Create, "/values/at", "2026-10-06T08:30:00+14:01" },
        { """{ "values": { "title": "Hi", "at": "2026-02-30T08:30:00Z" } }""", RecordOperation.Create, "/values/at", "2026-02-30T08:30:00Z" },
        { """{ "values": { "title": "Hi", "at": "2026-10-06 08:30:00Z" } }""", RecordOperation.Create, "/values/at", "2026-10-06 08:30:00Z" },

        // enum
        { """{ "values": { "title": "Hi", "status": "open" } }""", RecordOperation.Create, "/values/status", "open" },

        // reference
        { """{ "values": { "title": "Hi", "dept": "not-a-uuid" } }""", RecordOperation.Create, "/values/dept", "not-a-uuid" },
        { """{ "values": { "title": "Hi", "dept": "7c2e4f103b1a4d5e8f601a2b3c4d5e01" } }""", RecordOperation.Create, "/values/dept", "7c2e4f103b1a4d5e8f601a2b3c4d5e01" },
        { """{ "values": { "title": "Hi", "dept": "{7c2e4f10-3b1a-4d5e-8f60-1a2b3c4d5e01}" } }""", RecordOperation.Create, "/values/dept", "{7c2e4f10-3b1a-4d5e-8f60-1a2b3c4d5e01}" },

        // version on update
        { """{ "values": {} }""", RecordOperation.Update, "/version", null },
        { """{ "values": {}, "version": 1.5 }""", RecordOperation.Update, "/version", "1.5" },
        { """{ "values": {}, "version": 0 }""", RecordOperation.Update, "/version", null },
        { """{ "values": {}, "version": -1 }""", RecordOperation.Update, "/version", "-1" },
        { """{ "values": {}, "version": "1" }""", RecordOperation.Update, "/version", null },
        { """{ "values": {}, "version": null }""", RecordOperation.Update, "/version", null },
        { """{ "values": {}, "version": 1e1000000 }""", RecordOperation.Update, "/version", "1e1000000" },
        { """{ "values": {}, "version": 1e-9223372036854775808 }""", RecordOperation.Update, "/version", "1e-9223372036854775808" },
    };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void Body_with_one_problem_reports_only_its_pointer(string json, RecordOperation operation, string key, string? offending)
    {
        _ = offending;

        var result = Parse(json, operation);

        Assert.Null(result.Input);
        Assert.NotNull(result.Errors);
        Assert.Equal([key], result.Errors.Keys);
        Assert.Single(result.Errors[key]);
    }

    [Fact]
    public void Error_messages_name_no_storage_type_exception_or_request_text()
    {
        foreach (var row in ErrorCases)
        {
            var (json, operation, _, offending) = row.Data;
            var result = Parse(json, operation);

            Assert.NotNull(result.Errors);
            foreach (var message in result.Errors.Values.SelectMany(messages => messages))
            {
                Assert.All(_forbidden, forbidden => Assert.DoesNotContain(forbidden, message, StringComparison.Ordinal));
                if (offending is not null)
                {
                    Assert.DoesNotContain(offending, message, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void Invalid_utf8_in_a_property_name_or_a_value_is_one_body_error()
    {
        byte[][] bodies =
        [
            [.. "{ \"values\": { \"ti"u8, 0xC3, 0x28, .. "tle\": \"Hi\" } }"u8],
            [.. "{ \"values\": { \"title\": \"H"u8, 0xC3, 0x28, .. "i\" } }"u8],
            [.. "{ \"values\": { \"title\": \"H"u8, 0xFF, .. "i\" } }"u8],
            [.. "{ \"values\": { \"title\": \"H"u8, 0xED, 0xA0, 0x80, .. "i\" } }"u8],
        ];

        foreach (var body in bodies)
        {
            var result = RecordInputParser.Parse(body, _entity, RecordOperation.Create);

            Assert.Null(result.Input);
            Assert.NotNull(result.Errors);
            Assert.Equal([""], result.Errors.Keys);
        }
    }

    [Theory]
    [InlineData(
        RecordOperation.Create,
        """{ "colour": 1, "values": { "count": "5" }, "version": 1 }""")]
    [InlineData(
        RecordOperation.Update,
        """{ "version": 0, "values": { "title": null, "count": "5" }, "colour": 1 }""")]
    public void Body_with_several_problems_reports_them_all_in_ordinal_order(RecordOperation operation, string json)
    {
        var result = Parse(json, operation);

        Assert.Null(result.Input);
        Assert.NotNull(result.Errors);
        Assert.Equal(["/colour", "/values/count", "/values/title", "/version"], result.Errors.Keys);
    }

    [Fact]
    public void Valid_create_body_parses_every_field_type_in_declaration_order()
    {
        var result = Parse(
            """
            {
              "values": {
                "dept": "7C2E4F10-3B1A-4D5E-8F60-1A2B3C4D5E01",
                "status": "Open",
                "at": "2026-10-06t08:30:15.123456+07:00",
                "day": "2026-02-28",
                "active": true,
                "amount": 1.5e2,
                "total": 1.50,
                "count": 5.0,
                "title": "😀😀😀😀😀"
              }
            }
            """,
            RecordOperation.Create);

        Assert.Null(result.Errors);
        Assert.NotNull(result.Input);
        Assert.Null(result.Input.Version);
        Assert.Equal(_entity.Fields.Where(field => field.HasColumn), result.Input.Values.Select(value => value.Field));

        var values = result.Input.Values.Select(value => value.Value).ToList();
        Assert.Equal("😀😀😀😀😀", Assert.IsType<string>(values[0]));
        Assert.Equal(5L, Assert.IsType<long>(values[1]));
        Assert.Equal("1.50", Assert.IsType<string>(values[2]));
        Assert.Equal("150", Assert.IsType<string>(values[3]));
        Assert.True(Assert.IsType<bool>(values[4]));
        Assert.Equal(new DateOnly(2026, 2, 28), Assert.IsType<DateOnly>(values[5]));

        var at = Assert.IsType<DateTimeOffset>(values[6]);
        Assert.Equal(TimeSpan.Zero, at.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 8, 30, 15, TimeSpan.FromHours(7)).AddTicks(1_234_560), at);
        Assert.Equal(new DateTime(2026, 10, 6, 1, 30, 15, DateTimeKind.Utc).AddTicks(1_234_560), at.UtcDateTime);

        Assert.Equal("Open", Assert.IsType<string>(values[7]));
        Assert.Equal(_target.Id, Assert.IsType<Guid>(values[8]));
    }

    [Fact]
    public void Valid_update_body_has_only_the_named_fields_and_the_version()
    {
        var result = Parse("""{ "version": 7, "values": { "status": "Closed", "count": null } }""", RecordOperation.Update);

        Assert.Null(result.Errors);
        Assert.NotNull(result.Input);
        Assert.Equal(7L, result.Input.Version);
        Assert.Equal(
            new (string, object?)[] { ("count", null), ("status", "Closed") },
            result.Input.Values.Select(value => (value.Field.Name, value.Value)));
    }

    [Fact]
    public void Update_body_with_empty_values_only_carries_the_version()
    {
        var result = Parse("""{ "values": {}, "version": 9223372036854775807 }""", RecordOperation.Update);

        Assert.NotNull(result.Input);
        Assert.Empty(result.Input.Values);
        Assert.Equal(long.MaxValue, result.Input.Version);
    }

    [Fact]
    public void Null_for_a_field_that_is_not_required_is_kept_on_create()
    {
        var result = Parse("""{ "values": { "title": "Hi", "day": null } }""", RecordOperation.Create);

        Assert.NotNull(result.Input);
        Assert.Equal(new (string, object?)[] { ("title", "Hi"), ("day", null) }, result.Input.Values.Select(value => (value.Field.Name, value.Value)));
    }

    [Theory]
    [InlineData("5", 5L)]
    [InlineData("5.0", 5L)]
    [InlineData("5e0", 5L)]
    [InlineData("1.0e1", 10L)]
    [InlineData("100e-2", 1L)]
    [InlineData("-0", 0L)]
    [InlineData("0e-1000000000", 0L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Integral_numbers_are_integers(string number, long expected)
    {
        Assert.Equal(expected, Assert.IsType<long>(ParseValue("count", number)));
    }

    [Theory]
    [InlineData("1.50", "1.50")]
    [InlineData("1.5e2", "150")]
    [InlineData("1.50e1", "15.0")]
    [InlineData("1E+2", "100")]
    [InlineData("1e-3", "0.001")]
    [InlineData("-12.5e-1", "-1.25")]
    [InlineData("0.0001e10", "1000000")]
    [InlineData("-0", "0")]
    [InlineData("-0.00", "0.00")]
    [InlineData("0e1000000000", "0")]
    [InlineData("0e-1000000000", "0")]
    [InlineData("123456789012345678901234567890.123456789", "123456789012345678901234567890.123456789")]
    public void Decimals_are_plain_text_without_rounding(string number, string expected)
    {
        Assert.Equal(expected, Assert.IsType<string>(ParseValue("amount", number)));
    }

    [Fact]
    public void Decimal_without_precision_takes_postgresql_numeric_limits()
    {
        Assert.Equal($"0.{new string('0', 16382)}1", ParseValue("amount", "1e-16383"));
        Assert.Equal($"1{new string('0', 131071)}", ParseValue("amount", "1e131071"));
    }

    [Theory]
    [InlineData("999.99", "999.99")]
    [InlineData("-999.99", "-999.99")]
    [InlineData("1.500", "1.500")]
    [InlineData("0.01e2", "1")]
    public void Decimals_within_precision_and_scale_are_accepted(string number, string expected)
    {
        Assert.Equal(expected, Assert.IsType<string>(ParseValue("total", number)));
    }

    [Theory]
    [InlineData("2026-10-06T08:30:00Z", "2026-10-06T08:30:00.0000000+00:00")]
    [InlineData("2026-10-06T08:30:00.5z", "2026-10-06T08:30:00.5000000+00:00")]
    [InlineData("2026-10-06T08:30:00-14:00", "2026-10-06T22:30:00.0000000+00:00")]
    [InlineData("0001-01-01T00:00:00Z", "0001-01-01T00:00:00.0000000+00:00")]
    [InlineData("9999-12-31T23:59:59.999999Z", "9999-12-31T23:59:59.9999990+00:00")]
    [InlineData("0001-01-01T01:00:00+01:00", "0001-01-01T00:00:00.0000000+00:00")]
    public void Date_times_are_converted_to_utc(string text, string expected)
    {
        var value = Assert.IsType<DateTimeOffset>(ParseValue("at", $"\"{text}\""));

        Assert.Equal(TimeSpan.Zero, value.Offset);
        Assert.Equal(expected, value.ToString("O", CultureInfo.InvariantCulture));
    }

    private static object? ParseValue(string field, string json)
    {
        var result = Parse($$"""{ "values": { "title": "Hi", "{{field}}": {{json}} } }""", RecordOperation.Create);

        Assert.Null(result.Errors);
        Assert.NotNull(result.Input);
        return Assert.Single(result.Input.Values, value => value.Field.Name == field).Value;
    }

    private static RecordInputResult Parse(string json, RecordOperation operation) =>
        RecordInputParser.Parse(Encoding.UTF8.GetBytes(json), _entity, operation);
}
