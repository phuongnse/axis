using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class FieldRangeCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string TagId = "22222222-2222-4222-8222-222222222222";
    private const string LineId = "33333333-3333-4333-8333-333333333333";

    // Which limits each field type takes. A type that a later issue adds must be classified here.
    private static readonly Dictionary<FieldType, (bool Range, bool MinLength)> _limits = new()
    {
        [FieldType.Text] = (false, true),
        [FieldType.Integer] = (true, false),
        [FieldType.Decimal] = (true, false),
        [FieldType.Money] = (true, false),
        [FieldType.Boolean] = (false, false),
        [FieldType.Date] = (true, false),
        [FieldType.DateTime] = (true, false),
        [FieldType.Enum] = (false, false),
        [FieldType.Reference] = (false, false),
        [FieldType.References] = (false, false),
        [FieldType.ChildCollection] = (false, false),
        [FieldType.File] = (false, false),
    };

    // The field of each type with the properties it needs, without a limit.
    private static readonly Dictionary<FieldType, string> _declarations = new()
    {
        [FieldType.Text] = "\"type\": \"text\"",
        [FieldType.Integer] = "\"type\": \"integer\"",
        [FieldType.Decimal] = "\"type\": \"decimal\"",
        [FieldType.Money] = "\"type\": \"money\", \"currency\": \"USD\"",
        [FieldType.Boolean] = "\"type\": \"boolean\"",
        [FieldType.Date] = "\"type\": \"date\"",
        [FieldType.DateTime] = "\"type\": \"date-time\"",
        [FieldType.Enum] = "\"type\": \"enum\", \"values\": [\"a\", \"b\"]",
        [FieldType.Reference] = "\"type\": \"reference\", \"target\": \"Tag\"",
        [FieldType.References] = "\"type\": \"references\", \"target\": \"Tag\"",
        [FieldType.ChildCollection] = "\"type\": \"child-collection\", \"target\": \"Line\"",
        [FieldType.File] = "\"type\": \"file\"",
    };

    public static TheoryData<FieldType> Types => new(Enum.GetValues<FieldType>());

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_field_type_is_classified(FieldType type)
    {
        Assert.True(_limits.ContainsKey(type), $"Classify {type}: does it take min and max, or minLength?");
        Assert.True(_declarations.ContainsKey(type), $"Declare a field of type {type} for the tests.");
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Min_and_max_apply_only_to_the_types_that_take_a_range(FieldType type)
    {
        var (range, _) = _limits[type];
        var literal = type switch
        {
            FieldType.Date => "\"2026-01-01\"",
            FieldType.DateTime => "\"2026-01-01T00:00:00Z\"",
            _ => "1",
        };

        foreach (var property in new[] { "min", "max" })
        {
            using var folder = Folder($$"""{ "name": "limited", {{_declarations[type]}}, "{{property}}": {{literal}} }""");

            var result = ApplicationCompiler.Compile(folder.Path);

            if (range)
            {
                Assert.Empty(result.Diagnostics);
            }
            else
            {
                var diagnostic = Assert.Single(result.Diagnostics);
                Assert.Equal((DiagnosticCodes.InvalidConstraint, "entities/order.json", $"/fields/1/{property}"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void MinLength_applies_only_to_the_types_that_take_it(FieldType type)
    {
        var (_, minLength) = _limits[type];
        using var folder = Folder($$"""{ "name": "limited", {{_declarations[type]}}, "minLength": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        if (minLength)
        {
            Assert.Empty(result.Diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal((DiagnosticCodes.InvalidConstraint, "/fields/1/minLength"), (diagnostic.Code, diagnostic.Path));
        }
    }

    [Theory]
    [InlineData("integer", "min", "\"1\"")]
    [InlineData("integer", "max", "1.5")]
    [InlineData("integer", "min", "9223372036854775808")]
    [InlineData("decimal", "min", "\"1\"")]
    [InlineData("decimal", "max", "1e30")]
    [InlineData("date", "min", "5")]
    [InlineData("date", "max", "\"2026-13-01\"")]
    [InlineData("date", "min", "\"2026-01-01T00:00:00Z\"")]
    [InlineData("date-time", "min", "\"2026-01-01\"")]
    [InlineData("date-time", "max", "\"2026-01-01T00:00:00\"")]
    [InlineData("date-time", "min", "1")]
    public void A_limit_that_is_not_a_literal_of_the_field_type_is_an_invalid_constraint(string type, string property, string literal)
    {
        using var folder = Folder($$"""{ "name": "limited", "type": "{{type}}", "{{property}}": {{literal}} }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, $"/fields/1/{property}"), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("integer", "11", "10")]
    [InlineData("decimal", "0.51", "0.5")]
    [InlineData("decimal", "1e2", "99.9")]
    [InlineData("date", "\"2026-12-31\"", "\"2026-01-01\"")]
    [InlineData("date-time", "\"2026-01-01T02:00:00+00:00\"", "\"2026-01-01T01:00:00Z\"")]
    [InlineData("date-time", "\"2026-01-01T00:00:00Z\"", "\"2025-12-31T21:00:00-02:00\"")]
    public void Min_above_max_is_an_invalid_constraint_at_max(string type, string min, string max)
    {
        using var folder = Folder($$"""{ "name": "limited", "type": "{{type}}", "min": {{min}}, "max": {{max}} }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "/fields/1/max"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'max' must be at least 'min'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("integer", "10", "10")]
    [InlineData("decimal", "0.5", "0.50")]
    [InlineData("date", "\"2026-01-01\"", "\"2026-01-01\"")]
    [InlineData("date-time", "\"2026-01-01T01:00:00+01:00\"", "\"2026-01-01T00:00:00Z\"")]
    public void Min_equal_to_max_compiles(string type, string min, string max)
    {
        using var folder = Folder($$"""{ "name": "limited", "type": "{{type}}", "min": {{min}}, "max": {{max}} }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("""{ "name": "limited", "type": "text", "minLength": 5, "maxLength": 4 }""", "/fields/1/minLength")]
    [InlineData("""{ "name": "limited", "type": "text", "minLength": 0 }""", "/fields/1/minLength")]
    [InlineData("""{ "name": "limited", "type": "text", "minLength": 10485761 }""", "/fields/1/minLength")]
    public void An_invalid_minLength_is_an_invalid_constraint(string field, string path)
    {
        using var folder = Folder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, path), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("""{ "name": "limited", "type": "integer", "min": 1, "expression": "1 + 1" }""", "/fields/1/min")]
    [InlineData("""{ "name": "limited", "type": "integer", "max": 9, "expression": "1 + 1" }""", "/fields/1/max")]
    [InlineData("""{ "name": "limited", "type": "text", "minLength": 2, "creator": true }""", "/fields/1/minLength")]
    public void A_limit_on_a_field_the_server_sets_is_an_invalid_constraint(string field, string path)
    {
        using var folder = Folder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, path), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("the server sets", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("integer", "[1]")]
    [InlineData("date", "true")]
    public void A_limit_that_is_not_a_number_or_a_string_is_a_schema_violation(string type, string literal)
    {
        using var folder = Folder($$"""{ "name": "limited", "type": "{{type}}", "min": {{literal}} }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
        Assert.NotEmpty(result.Diagnostics);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Limits_are_on_the_field_model_as_written()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "count", "type": "integer", "min": 1, "max": 10 },
                    { "name": "amount", "type": "decimal", "precision": 10, "scale": 2, "min": 0.5, "max": 99.50 },
                    { "name": "takenOn", "type": "date", "min": "2026-01-01", "max": "2026-12-31" },
                    { "name": "at", "type": "date-time", "min": "2026-01-01T00:00:00Z" },
                    { "name": "code", "type": "text", "minLength": 3, "maxLength": 10 },
                    { "name": "plain", "type": "text" }
                  ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        var fields = order.Fields.ToDictionary(field => field.Name);
        Assert.Equal(("1", "10"), (fields["count"].Min, fields["count"].Max));
        Assert.Equal(("0.5", "99.50"), (fields["amount"].Min, fields["amount"].Max));
        Assert.Equal(("2026-01-01", "2026-12-31"), (fields["takenOn"].Min, fields["takenOn"].Max));
        Assert.Equal(("2026-01-01T00:00:00Z", null), (fields["at"].Min, fields["at"].Max));
        Assert.Equal((3, 10), (fields["code"].MinLength, fields["code"].MaxLength));
        Assert.Equal((null, null, null), (fields["plain"].Min, fields["plain"].Max, fields["plain"].MinLength));
    }

    /// <summary>
    /// An Order entity with <c>title</c>, then <paramref name="field"/> at index 1. The entities
    /// Tag, which a reference can point to, and Line, which a child collection can, are loaded too.
    /// </summary>
    private static TemporaryFolder Folder(string field) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    {{field}}
                  ] }
                """)
            .With("entities/tag.json", $$"""
                { "id": "{{TagId}}", "kind": "entity", "name": "Tag", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/line.json", $$"""
                { "id": "{{LineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [ { "name": "description", "type": "text" } ] }
                """);
}
