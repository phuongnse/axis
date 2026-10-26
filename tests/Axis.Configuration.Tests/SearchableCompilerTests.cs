using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class SearchableCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    [Fact]
    public void Searchable_text_field_is_on_the_field_model()
    {
        using var folder = Folder(
            """{ "name": "note", "type": "text", "searchable": true }""",
            """{ "name": "remark", "type": "text", "searchable": false }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(["note"], order.Fields.Where(field => field.Searchable).Select(field => field.Name));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Searchable_on_an_integer_field_is_an_invalid_constraint_even_when_false(string value)
    {
        using var folder = Folder($$"""{ "name": "amount", "type": "integer", "searchable": {{value}} }""", null);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "entities/order.json", "/fields/1/searchable"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Searchable_that_is_not_a_boolean_is_a_schema_violation()
    {
        using var folder = Folder("""{ "name": "note", "type": "text", "searchable": "yes" }""", null);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code);
        Assert.Null(result.Model);
    }

    /// <summary>An Order entity with <c>title</c>, then <paramref name="field"/> at index 1 and <paramref name="field2"/> when given.</summary>
    private static TemporaryFolder Folder(string field, string? field2) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    {{field}}{{(field2 is null ? "" : $", {field2}")}}
                  ] }
                """);
}
