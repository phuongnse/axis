using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class SequenceCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string LineId = "33333333-3333-4333-8333-333333333333";
    private const string SequenceId = "44444444-4444-4444-8444-444444444444";

    [Fact]
    public void Field_naming_no_loaded_sequence_is_an_unknown_sequence_and_gives_no_model()
    {
        using var folder = Folder("""{ "name": "number", "type": "text", "sequence": "InvoiceNumber" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownSequence, "entities/order.json", "/fields/2/sequence"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'InvoiceNumber'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(OrderId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Valid_sequence_named_in_another_letter_case_is_on_the_field_model()
    {
        using var folder = Folder("""{ "name": "number", "type": "text", "maxLength": 20, "required": false, "sequence": "purchaserequestNUMBER" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("number", out var number));
        Assert.NotNull(number.Sequence);
        Assert.Equal(
            (Guid.Parse(SequenceId), "PurchaseRequestNumber", "PR-{yyyy}-{n:5}"),
            (number.Sequence.Id, number.Sequence.Name, number.Sequence.Format));
        Assert.All(order.Fields.Where(field => field.Name != "number"), field => Assert.Null(field.Sequence));
    }

    [Theory]
    [InlineData("""{ "name": "number", "type": "integer", "sequence": "PurchaseRequestNumber" }""", null, "integer")]
    [InlineData("""{ "name": "number", "type": "text", "required": true, "sequence": "PurchaseRequestNumber" }""", null, "required")]
    [InlineData("""{ "name": "number", "type": "text", "expression": "title", "sequence": "PurchaseRequestNumber" }""", null, "expression")]
    [InlineData(null, """{ "name": "number", "type": "text", "sequence": "PurchaseRequestNumber" }""", "Order.lines")]
    public void Sequence_on_a_field_that_cannot_have_one_is_an_invalid_constraint(string? orderField, string? lineField, string messagePart)
    {
        using var folder = Folder(orderField, lineField);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        var (file, resourceId) = orderField is null ? ("entities/line.json", LineId) : ("entities/order.json", OrderId);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, file, "/fields/2/sequence"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(resourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Sequence_on_a_field_that_is_not_text_is_not_looked_up()
    {
        using var folder = Folder("""{ "name": "number", "type": "integer", "sequence": "InvoiceNumber" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "/fields/2/sequence"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("PR-{yyyy}")]
    [InlineData("PR-{n:19}")]
    [InlineData("PR-{n:0}")]
    [InlineData("{n}{n}")]
    [InlineData("PR-{x}-{n}")]
    public void Format_without_exactly_one_valid_number_token_is_a_schema_violation_and_is_not_reported_again(string format)
    {
        using var folder = Folder("""{ "name": "number", "type": "text", "sequence": "PurchaseRequestNumber" }""", format: format);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "sequences/pr-number.json", "/format"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.UnknownSequence);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("{n}")]
    [InlineData("{n:18}")]
    [InlineData("{yyyy}{n:1}")]
    [InlineData("{yyyy}/{n:4}/{yyyy}")]
    public void Format_with_one_number_token_compiles(string format)
    {
        using var folder = Folder("""{ "name": "number", "type": "text", "sequence": "PurchaseRequestNumber" }""", format: format);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("number", out var number));
        Assert.Equal(format, number.Sequence!.Format);
    }

    /// <summary>
    /// An Order entity with <c>title</c> and a <c>lines</c> child collection, then
    /// <paramref name="orderField"/> at index 2. Its child entity Line has <c>description</c> and
    /// <c>quantity</c>, then <paramref name="lineField"/> at index 2. The PurchaseRequestNumber
    /// sequence has <paramref name="format"/>.
    /// </summary>
    private static TemporaryFolder Folder(string? orderField, string? lineField = null, string format = "PR-{yyyy}-{n:5}") =>
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
            .With("sequences/pr-number.json", $$"""
                { "id": "{{SequenceId}}", "kind": "sequence", "name": "PurchaseRequestNumber", "formatVersion": 1,
                  "format": "{{format}}" }
                """);
}
