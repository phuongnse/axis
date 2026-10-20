using Axis.Configuration.Compilation;
using Axis.Configuration.Releases;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Tests;

public sealed class ExpressionProbeTests
{
    private const string Order = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [
            { "name": "quantity", "type": "integer" },
            { "name": "unitPrice", "type": "decimal" },
            { "name": "total", "type": "decimal", "expression": "quantity * unitPrice" }
          ],
          "validations": [
            { "field": "quantity", "message": { "textKey": "order.quantity" }, "expression": "quantity > 0" }
          ] }
        """;

    private const string Texts = """
        { "id": "22222222-2222-4222-8222-222222222221", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "order.quantity": "Quantity must be positive" } }
        """;

    [Fact]
    public void A_validation_probe_records_a_boolean_and_no_problem()
    {
        var probe = new ExpressionProbe("entities/order.json", "/validations/0/expression");

        var result = Compile(probe);

        Assert.Empty(result.Diagnostics);
        Assert.True(probe.Found);
        Assert.Equal(ExpressionTypeKind.Boolean, probe.Type!.Kind);
        Assert.Null(probe.Diagnostic);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Path == "/validations/0/expression");
    }

    [Fact]
    public void A_computed_field_probe_records_the_fields_type()
    {
        var probe = new ExpressionProbe("entities/order.json", "/fields/2/expression");

        Compile(probe);

        Assert.True(probe.Found);
        Assert.Equal(ExpressionTypeKind.Decimal, probe.Type!.Kind);
        Assert.Null(probe.Diagnostic);
    }

    [Fact]
    public void A_probe_records_the_problem_and_no_type()
    {
        var probe = new ExpressionProbe("entities/order.json", "/validations/0/expression");

        Compile(probe, Order.Replace("quantity > 0", "quantity > 'x'", StringComparison.Ordinal));

        Assert.True(probe.Found);
        Assert.Null(probe.Type);
        Assert.Equal("AXC0047", probe.Diagnostic!.Code);
    }

    [Fact]
    public void A_probe_at_a_path_that_holds_no_expression_finds_nothing()
    {
        var probe = new ExpressionProbe("entities/order.json", "/name");

        Compile(probe);

        Assert.False(probe.Found);
    }

    private static CompilationResult Compile(ExpressionProbe probe, string order = Order) =>
        ApplicationCompiler.CompileFiles(
            [
                new ResourceContent("application.json", PresentationCompilerTests.Manifest),
                new ResourceContent("entities/order.json", order),
                new ResourceContent("texts/en.json", Texts),
            ],
            null,
            null,
            probe);
}
