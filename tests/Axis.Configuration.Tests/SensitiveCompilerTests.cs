using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class SensitiveCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string LineId = "33333333-3333-4333-8333-333333333333";

    [Fact]
    public void Sensitive_text_and_decimal_fields_are_on_the_field_model()
    {
        using var folder = Folder(
            """{ "name": "secret", "type": "text", "sensitive": true }""",
            """{ "name": "cost", "type": "decimal", "precision": 10, "scale": 2, "sensitive": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(["secret", "cost"], order.Fields.Where(field => field.Sensitive).Select(field => field.Name));
    }

    [Fact]
    public void Sensitive_field_of_a_child_entity_compiles()
    {
        using var folder = Folder(null, null, """{ "name": "note", "type": "text", "sensitive": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Line", out var line));
        Assert.Equal(["note"], line.Fields.Where(field => field.Sensitive).Select(field => field.Name));
    }

    [Fact]
    public void Sensitive_that_is_not_a_boolean_is_a_schema_violation()
    {
        using var folder = Folder("""{ "name": "secret", "type": "text", "sensitive": "yes" }""", null);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Sensitive_on_a_child_collection_field_is_an_invalid_constraint_even_when_false()
    {
        using var folder = Folder(null, null, null, """{ "name": "lines", "type": "child-collection", "target": "Line", "sensitive": false }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "entities/order.json", "/fields/1/sensitive"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Exact_lookup_without_sensitive_is_reported_even_when_false(string value)
    {
        using var folder = Folder($$"""{ "name": "nationalId", "type": "text", "exactLookup": {{value}} }""", null);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ExactLookupUse, "entities/order.json", "/fields/2/exactLookup"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Exact_lookup_on_a_sensitive_field_is_on_the_field_model()
    {
        using var folder = Folder(
            """{ "name": "nationalId", "type": "text", "sensitive": true, "exactLookup": true }""",
            """{ "name": "note", "type": "text", "sensitive": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(["nationalId"], order.Fields.Where(field => field.ExactLookup).Select(field => field.Name));
    }

    [Fact]
    public void Exact_lookup_on_a_field_whose_name_is_too_long_for_the_lookup_column_is_reported()
    {
        var longest = new string('a', ApplicationCompiler.MaxExactLookupFieldNameLength);
        using var fits = Folder($$"""{ "name": "{{longest}}", "type": "text", "sensitive": true, "exactLookup": true }""", null);
        using var tooLong = Folder($$"""{ "name": "{{longest}}b", "type": "text", "sensitive": true, "exactLookup": true }""", null);

        Assert.Empty(ApplicationCompiler.Compile(fits.Path).Diagnostics);
        var diagnostic = Assert.Single(ApplicationCompiler.Compile(tooLong.Path).Diagnostics);
        Assert.Equal((DiagnosticCodes.ExactLookupUse, "/fields/2/exactLookup"), (diagnostic.Code, diagnostic.Path));
    }

    /// <summary>
    /// An Order entity with <c>title</c>, then <paramref name="linesField"/> (by default a plain
    /// <c>lines</c> child collection) at index 1, then the two optional fields. Its child entity
    /// Line has <c>description</c>, then <paramref name="lineField"/> when given.
    /// </summary>
    private static TemporaryFolder Folder(string? orderField, string? orderField2, string? lineField = null, string? linesField = null) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    {{linesField ?? """{ "name": "lines", "type": "child-collection", "target": "Line" }"""}}{{(orderField is null ? "" : $", {orderField}")}}{{(orderField2 is null ? "" : $", {orderField2}")}}
                  ] }
                """)
            .With("entities/line.json", $$"""
                { "id": "{{LineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [
                    { "name": "description", "type": "text" }{{(lineField is null ? "" : $", {lineField}")}}
                  ] }
                """);
}
