using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Evaluation;

namespace Axis.Configuration.Tests;

public sealed class RuleCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "order.quantityPositive": "Quantity must be greater than zero." } }
        """;

    private const string IsPositive = """
        { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "IsPositive", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "value > 0" }
        """;

    [Fact]
    public void A_validation_that_calls_a_rule_compiles_and_evaluates_with_the_rule_result()
    {
        using var folder = Folder("IsPositive(quantity)").With("rules/is-positive.json", IsPositive);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        var validation = order.Validations[0];
        Assert.Equal(true, Evaluate(validation, 3L));
        Assert.Equal(false, Evaluate(validation, 0L));
    }

    [Fact]
    public void A_rule_can_call_a_rule_in_a_later_file()
    {
        // The caller sorts first in path order, so its callee is checked before it.
        using var folder = Folder("ISLARGE(quantity)")
            .With("rules/a-is-large.json", Rule("66666666-6666-4666-8666-666666666602", "IsLarge", "isPositive(value - 9)"))
            .With("rules/b-is-positive.json", IsPositive);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(true, Evaluate(order.Validations[0], 10L));
        Assert.Equal(false, Evaluate(order.Validations[0], 9L));
    }

    [Theory]
    [InlineData("Nope(quantity)", ExpressionDiagnosticCodes.UnknownFunction, "Unknown function or rule 'Nope' at character 1.")]
    [InlineData("IsPositive()", ExpressionDiagnosticCodes.WrongArgumentCount, "Rule 'IsPositive' needs 1 argument, found 0 at character 1.")]
    [InlineData("IsPositive('a')", ExpressionDiagnosticCodes.TypeMismatch, "Rule 'IsPositive' needs integer for argument 1, found text at character 1.")]
    public void A_wrong_rule_call_is_reported_at_the_validation_and_gives_no_model(string expression, string code, string message)
    {
        using var folder = Folder(expression).With("rules/is-positive.json", IsPositive);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "entities/order.json", "/validations/0/expression"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(message, diagnostic.Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Rules_that_call_each_other_are_one_cycle_reported_at_the_first_rule()
    {
        using var folder = Folder("A(quantity)")
            .With("rules/a.json", Rule("66666666-6666-4666-8666-666666666603", "A", "b(value)"))
            .With("rules/b.json", Rule("66666666-6666-4666-8666-666666666604", "B", "A(value) or B(value)"));

        var result = ApplicationCompiler.Compile(folder.Path);

        // B calling itself is a second cycle, reported at B.
        Assert.Equal(
            [
                (DiagnosticCodes.RuleCallCycle, "rules/a.json", "/expression", "Rules 'A' → 'B' → 'A' call each other in a cycle."),
                (DiagnosticCodes.RuleCallCycle, "rules/b.json", "/expression", "Rules 'B' → 'B' call each other in a cycle."),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message)));
        Assert.Equal(Guid.Parse("66666666-6666-4666-8666-666666666603"), result.Diagnostics[0].ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_cycle_of_two_rules_is_reported_once_naming_both()
    {
        using var folder = Folder(null)
            .With("rules/a.json", Rule("66666666-6666-4666-8666-666666666603", "A", "B(value) and B(value + 1)"))
            .With("rules/b.json", Rule("66666666-6666-4666-8666-666666666604", "B", "A(value)"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.RuleCallCycle, "rules/a.json", "/expression"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'A'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'B'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_validation_that_calls_a_chain_of_8_rules_compiles()
    {
        using var folder = Chain(Folder("R1(quantity)"), 8);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_chain_of_9_rules_is_reported_once_at_the_first_rule_naming_the_chain()
    {
        using var folder = Chain(Folder("R1(quantity)"), 9);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.RuleCallTooDeep, "rules/r01.json", "/expression"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            "Rules 'R1' → 'R2' → 'R3' → 'R4' → 'R5' → 'R6' → 'R7' → 'R8' → 'R9' nest calls 9 deep, past the limit of 8.",
            diagnostic.Message);
        Assert.Equal(Guid.Parse("66666666-6666-4666-8666-666666666701"), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_chain_of_10_rules_is_reported_once_at_the_lowest_rule_past_the_limit()
    {
        using var folder = Chain(Folder("R1(quantity)"), 10);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.RuleCallTooDeep, "rules/r02.json", "/expression"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.StartsWith("Rules 'R2' → 'R3'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'R9' → 'R10' nest calls 9 deep", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_long_cycle_and_a_rule_that_calls_into_it_are_only_a_cycle()
    {
        // R10 calls R1, so R1 to R10 are one cycle of 10 rules, and Outer calls into it.
        using var folder = Chain(Folder("Outer(quantity)"), 10, "R1(value)")
            .With("rules/outer.json", Rule("66666666-6666-4666-8666-666666666799", "Outer", "R1(value)"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.RuleCallCycle, diagnostic.Code));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.RuleCallTooDeep);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_repeated_parameter_name_is_reported_at_the_later_parameter()
    {
        using var folder = Folder(null).With("rules/both.json", """
            { "id": "66666666-6666-4666-8666-666666666605", "kind": "rule", "name": "Both", "formatVersion": 1,
              "parameters": [{ "name": "value", "type": "integer" }, { "name": "Value", "type": "integer" }],
              "resultType": "boolean", "expression": "value > 0" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateRuleParameter, "rules/both.json", "/parameters/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Value'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("round")]
    // An aggregate is a built-in function too.
    [InlineData("sum")]
    [InlineData("Count")]
    public void A_rule_named_like_a_built_in_function_is_reported_at_its_name(string name)
    {
        using var folder = Folder(null).With($"rules/{name}.json", Rule("66666666-6666-4666-8666-666666666606", name, "value > 0"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.RuleNameIsFunction, $"rules/{name}.json", "/name"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    // A rule body sees only its parameters, never the fields of an entity.
    [InlineData("quantity > 0", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("value + 1", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("value >", ExpressionDiagnosticCodes.SyntaxError)]
    public void A_problem_in_a_rule_body_is_reported_at_its_expression_and_not_at_its_callers(string expression, string code)
    {
        using var folder = Folder("Check(quantity)").With("rules/check.json", Rule("66666666-6666-4666-8666-666666666607", "Check", expression));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "rules/check.json", "/expression"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_enum_parameter_type_is_a_schema_violation()
    {
        using var folder = Folder(null).With("rules/status.json", """
            { "id": "66666666-6666-4666-8666-666666666608", "kind": "rule", "name": "IsOpen", "formatVersion": 1,
              "parameters": [{ "name": "status", "type": "enum" }], "resultType": "boolean", "expression": "status == 'open'" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "rules/status.json", "/parameters/0/type"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_computed_field_cannot_call_a_rule_yet()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("rules/is-positive.json", IsPositive)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "quantity", "type": "integer" },
                    { "name": "positive", "type": "boolean", "expression": "IsPositive(quantity)" }
                  ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownFunction, "entities/order.json", "/fields/1/expression"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private static object? Evaluate(Model.ValidationModel validation, long quantity)
    {
        var result = ExpressionInterpreter.Evaluate(
            validation.Syntax, validation.Check, new ExpressionValues([KeyValuePair.Create("quantity", (object?)quantity)]));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    /// <summary>A rule with one integer parameter, <c>value</c>, that gives a boolean.</summary>
    private static string Rule(string id, string name, string expression) => $$"""
        { "id": "{{id}}", "kind": "rule", "name": "{{name}}", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "{{expression}}" }
        """;

    /// <summary>
    /// Adds the rules <c>R1</c> to <c>R{count}</c>, where each calls the next and the last gives
    /// <paramref name="last"/>.
    /// </summary>
    private static TemporaryFolder Chain(TemporaryFolder folder, int count, string last = "value > 0")
    {
        for (var k = 1; k <= count; k++)
        {
            var expression = k < count ? $"R{k + 1}(value)" : last;
            folder.With($"rules/r{k:D2}.json", Rule($"66666666-6666-4666-8666-6666666667{k:D2}", $"R{k}", expression));
        }

        return folder;
    }

    /// <summary>An application with an <c>Order</c> entity, and one validation on its <c>quantity</c> unless <paramref name="validation"/> is null.</summary>
    private static TemporaryFolder Folder(string? validation)
    {
        var validations = validation is null
            ? ""
            : $$""", "validations": [{ "expression": "{{validation}}", "message": { "textKey": "order.quantityPositive" }, "field": "quantity" }]""";
        return new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ]{{validations}} }
                """);
    }
}
