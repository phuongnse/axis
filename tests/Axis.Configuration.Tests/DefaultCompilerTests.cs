using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class DefaultCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string LineId = "33333333-3333-4333-8333-333333333333";
    private const string SequenceId = "44444444-4444-4444-8444-444444444444";

    [Fact]
    public void Default_calling_a_rule_compiles_and_is_on_the_field_model()
    {
        using var folder = Folder("""{ "name": "doubled", "type": "integer", "default": "Twice(21)" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("doubled", out var doubled));
        Assert.NotNull(doubled.Default);
        Assert.Equal("Twice(21)", doubled.Default.Expression);
        Assert.All(order.Fields.Where(field => field.Name != "doubled"), field => Assert.Null(field.Default));
    }

    [Fact]
    public void Default_calling_now_on_a_date_time_field_compiles()
    {
        using var folder = Folder("""{ "name": "openedAt", "type": "date-time", "default": "now()" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("openedAt", out var openedAt));
        Assert.NotNull(openedAt.Default);
    }

    [Fact]
    public void Default_on_a_child_entity_field_compiles()
    {
        using var folder = Folder(null, """{ "name": "note", "type": "text", "default": "'none'" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "count", "type": "integer", "default": "'abc'" }""", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("""{ "name": "count", "type": "integer", "default": "title" }""", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("""{ "name": "count", "type": "integer", "default": "count" }""", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("""{ "name": "level", "type": "enum", "values": ["low", "high"], "default": "'middle'" }""", ExpressionDiagnosticCodes.UnknownEnumValue)]
    [InlineData("""{ "name": "day", "type": "date", "default": "now()" }""", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    public void Default_with_an_expression_problem_reports_the_expression_code(string orderField, string code)
    {
        using var folder = Folder(orderField);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "entities/order.json", "/fields/2/default"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(Guid.Parse(OrderId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "total", "type": "integer", "expression": "1", "default": "1" }""", "expression")]
    [InlineData("""{ "name": "number", "type": "text", "sequence": "PurchaseRequestNumber", "default": "'x'" }""", "sequence")]
    [InlineData("""{ "name": "requester", "type": "text", "creator": true, "default": "'x'" }""", "creator")]
    [InlineData("""{ "name": "customer", "type": "reference", "target": "Customer", "default": "null" }""", "reference")]
    [InlineData("""{ "name": "customers", "type": "references", "target": "Customer", "default": "null" }""", "references")]
    [InlineData("""{ "name": "more", "type": "child-collection", "target": "Note", "default": "null" }""", "child-collection")]
    public void Default_that_clashes_is_a_single_invalid_constraint(string orderField, string messagePart)
    {
        using var folder = Folder(orderField);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "entities/order.json", "/fields/2/default"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(OrderId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Default_with_creator_false_compiles()
    {
        using var folder = Folder("""{ "name": "requester", "type": "text", "creator": false, "default": "'x'" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    public static TheoryData<FieldType> AllFieldTypes() => [.. Enum.GetValues<FieldType>()];

    [Theory]
    [MemberData(nameof(AllFieldTypes))]
    public void Default_compiles_exactly_on_the_types_that_allow_it(FieldType type)
    {
        var (field, allowed) = type switch
        {
            FieldType.Text => ("""{ "name": "extra", "type": "text", "default": "'x'" }""", true),
            FieldType.Integer => ("""{ "name": "extra", "type": "integer", "default": "1" }""", true),
            FieldType.Decimal => ("""{ "name": "extra", "type": "decimal", "default": "1.5" }""", true),
            FieldType.Money => ("""{ "name": "extra", "type": "money", "currency": "VND", "default": "1500" }""", true),
            FieldType.Boolean => ("""{ "name": "extra", "type": "boolean", "default": "true" }""", true),
            FieldType.Date => ("""{ "name": "extra", "type": "date", "default": "date('2026-01-01')" }""", true),
            FieldType.DateTime => ("""{ "name": "extra", "type": "date-time", "default": "now()" }""", true),
            FieldType.Enum => ("""{ "name": "extra", "type": "enum", "values": ["low", "high"], "default": "'low'" }""", true),
            FieldType.Reference => ("""{ "name": "extra", "type": "reference", "target": "Customer", "default": "null" }""", false),
            FieldType.References => ("""{ "name": "extra", "type": "references", "target": "Customer", "default": "null" }""", false),
            FieldType.ChildCollection => ("""{ "name": "extra", "type": "child-collection", "target": "Note", "default": "null" }""", false),
            FieldType.File => ("""{ "name": "extra", "type": "file", "default": "null" }""", false),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Add this field type to the test."),
        };
        Assert.Contains($"\"{FieldTypes.Name(type)}\"", field, StringComparison.Ordinal);
        using var folder = Folder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        if (allowed)
        {
            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Model);
        }
        else
        {
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("/fields/2/default", diagnostic.Path);
            Assert.Equal(DiagnosticCodes.InvalidConstraint, diagnostic.Code);
        }
    }

    /// <summary>
    /// An Order entity with <c>title</c> and a <c>lines</c> child collection, then
    /// <paramref name="orderField"/> at index 2. Its child entity Line has <c>description</c> and
    /// <c>quantity</c>, then <paramref name="lineField"/> at index 2. Customer, Note (a child entity
    /// that no entity owns yet), the PurchaseRequestNumber sequence and the <c>Twice</c> rule are loaded.
    /// </summary>
    private static TemporaryFolder Folder(string? orderField, string? lineField = null) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "lines", "type": "child-collection", "target": "Line" }{{(orderField is null ? "" : $", {orderField}")}}
                  ] }
                """)
            .With("entities/line.json", $$"""
                { "id": "{{LineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [
                    { "name": "description", "type": "text" },
                    { "name": "quantity", "type": "integer" }{{(lineField is null ? "" : $", {lineField}")}}
                  ] }
                """)
            .With("entities/customer.json", """
                { "id": "22222222-2222-4222-8222-222222222202", "kind": "entity", "name": "Customer", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/note.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "entity", "name": "Note", "formatVersion": 1,
                  "fields": [ { "name": "text", "type": "text" } ] }
                """)
            .With("rules/twice.json", """
                { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "Twice", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "integer" }], "resultType": "integer", "expression": "value * 2" }
                """)
            .With("sequences/pr-number.json", $$"""
                { "id": "{{SequenceId}}", "kind": "sequence", "name": "PurchaseRequestNumber", "formatVersion": 1,
                  "format": "PR-{yyyy}-{n:5}" }
                """);
}
