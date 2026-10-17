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
