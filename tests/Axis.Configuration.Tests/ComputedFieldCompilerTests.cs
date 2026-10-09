using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Tests;

public sealed class ComputedFieldCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    public static TheoryData<string, string, string, string> InvalidComputedFields => new()
    {
        { """{ "name": "label", "type": "text", "expression": "quantity * 2" }""", ExpressionDiagnosticCodes.ResultTypeMismatch, "/fields/4/expression", "text" },
        { """{ "name": "total", "type": "decimal", "required": true, "expression": "quantity * price" }""", DiagnosticCodes.InvalidConstraint, "/fields/4/required", "computed" },
        { """{ "name": "other", "type": "reference", "target": "Supplier", "expression": "supplier" }""", DiagnosticCodes.InvalidConstraint, "/fields/4/expression", "reference" },
        { """{ "name": "twice", "type": "integer", "expression": "twice + 1" }""", ExpressionDiagnosticCodes.UnknownName, "/fields/4/expression", "'twice'" },
        { """{ "name": "total", "type": "decimal", "expression": "quantity *" }""", ExpressionDiagnosticCodes.SyntaxError, "/fields/4/expression", "character" },
        { """{ "name": "state", "type": "enum", "values": ["a", "b"], "expression": "status" }""", ExpressionDiagnosticCodes.ResultTypeMismatch, "/fields/4/expression", "enum" },
    };

    [Theory]
    [MemberData(nameof(InvalidComputedFields))]
    public void Invalid_computed_field_is_reported_at_its_path_and_gives_no_model(string field, string code, string path, string messagePart)
    {
        using var folder = Folder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "entities/order.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(OrderId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Computed_field_naming_another_computed_field_is_an_unknown_name()
    {
        using var folder = Folder(
            """{ "name": "total", "type": "decimal", "expression": "quantity * price" }""",
            """{ "name": "doubled", "type": "decimal", "expression": "total * 2" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.UnknownName, "/fields/5/expression"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'total'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_reads_a_computed_field()
    {
        using var folder = Folder(["""{ "name": "total", "type": "decimal", "expression": "quantity * price" }"""],
            validations: """[{ "expression": "total is null or total > 0", "message": { "textKey": "order.totalPositive" }, "field": "total" }]""")
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "order.totalPositive": "The total must be greater than zero." } }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Valid_computed_field_is_marked_in_the_model_and_changes_the_content_hash()
    {
        using var plain = Folder("""{ "name": "total", "type": "decimal", "precision": 18, "scale": 2 }""");
        // An integer result fits a decimal field, and a name matches ignoring letter case.
        using var computed = Folder("""{ "name": "total", "type": "decimal", "precision": 18, "scale": 2, "expression": "Quantity * price" }""");

        var before = ApplicationCompiler.Compile(plain.Path);
        var result = ApplicationCompiler.Compile(computed.Path);

        Assert.Empty(before.Diagnostics);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.HasComputedFields);
        var total = Assert.Single(order.Fields, field => field.IsComputed);
        Assert.Equal("total", total.Name);
        Assert.Equal("Quantity * price", total.Computed!.Expression);
        Assert.True(total.Computed.Check.Succeeded);
        Assert.Equal(ExpressionType.Decimal, total.Computed.Check.Type);
        Assert.All(order.Fields.Where(field => field.Name != "total"), field => Assert.False(field.IsComputed));
        Assert.NotNull(before.ContentHash);
        Assert.NotNull(result.ContentHash);
        Assert.NotEqual(before.ContentHash, result.ContentHash);
    }

    [Fact]
    public void Computed_field_can_aggregate_the_computed_values_of_its_child_rows()
    {
        using var folder = Folder(
            """{ "name": "lines", "type": "child-collection", "target": "Line" }""",
            """{ "name": "total", "type": "decimal", "expression": "sum(lines, amount)" }""",
            """{ "name": "lineCount", "type": "integer", "expression": "count(lines, quantity > 0)" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("total", out var total));
        Assert.Equal(ExpressionType.Decimal, total.Computed!.Check.Type);
        Assert.True(order.TryGetField("lineCount", out var lineCount));
        Assert.Equal(ExpressionType.Integer, lineCount.Computed!.Check.Type);
    }

    [Theory]
    [InlineData("sum(lines, missing)", ExpressionDiagnosticCodes.UnknownName, "'missing'")]
    // The item expression sees only the child row, not the owner's fields.
    [InlineData("sum(lines, price)", ExpressionDiagnosticCodes.UnknownName, "'price'")]
    [InlineData("sum(quantity, 1)", ExpressionDiagnosticCodes.TypeMismatch, "child collection")]
    [InlineData("if(lines == null, 0, 1)", ExpressionDiagnosticCodes.TypeMismatch, "list<Line>")]
    public void Wrong_aggregate_in_a_computed_field_is_reported_at_its_expression(string expression, string code, string messagePart)
    {
        using var folder = Folder(
            """{ "name": "lines", "type": "child-collection", "target": "Line" }""",
            $$"""{ "name": "total", "type": "decimal", "expression": "{{expression}}" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "/fields/5/expression"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
    }

    private static TemporaryFolder Folder(params string[] extraFields) => Folder(extraFields, validations: null);

    /// <summary>
    /// An Order entity with quantity, price, status and supplier, then <paramref name="extraFields"/>
    /// from index 4. A Line entity with a computed <c>amount</c> can be its child.
    /// </summary>
    private static TemporaryFolder Folder(string[] extraFields, string? validations) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "quantity", "type": "integer" },
                    { "name": "price", "type": "decimal", "precision": 18, "scale": 2 },
                    { "name": "status", "type": "enum", "values": ["open", "closed"] },
                    { "name": "supplier", "type": "reference", "target": "Supplier" },
                    {{string.Join(", ", extraFields)}}
                  ]{{(validations is null ? "" : $", \"validations\": {validations}")}} }
                """)
            .With("entities/supplier.json", """
                { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Supplier", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/line.json", """
                { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [
                    { "name": "quantity", "type": "integer" },
                    { "name": "unitPrice", "type": "decimal", "precision": 18, "scale": 2 },
                    { "name": "amount", "type": "decimal", "expression": "quantity * unitPrice" }
                  ] }
                """);
}
