using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Evaluation;

namespace Axis.Configuration.Tests;

public sealed class ProcessCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "order.cannotStart": "This order cannot be started." } }
        """;

    private const string NeedsReview = """
        { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "NeedsReview", "formatVersion": 1,
          "parameters": [{ "name": "total", "type": "decimal" }], "resultType": "boolean", "expression": "total >= 10000" }
        """;

    private const string ReviewSteps = """
        [
          { "name": "check", "type": "decision", "branches": [{ "when": "NeedsReview(amount)", "next": "big" }], "otherwise": "small" },
          { "name": "big", "type": "end" },
          { "name": "small", "type": "end" }
        ]
        """;

    [Fact]
    public void A_process_with_a_decision_that_calls_a_threshold_rule_compiles_into_the_model()
    {
        using var folder = Folder(Process("Order", ReviewSteps, startCondition: "status is null"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetProcess("orderReview", out var process));
        Assert.Equal(
            (Guid.Parse(ProcessId), "OrderReview", "processes/order-review.json", new EntityReference(Guid.Parse(OrderId), "Order"), "check"),
            (process.Id, process.Name, process.File, process.Entity, process.Start));
        Assert.NotNull(process.StartCondition);
        Assert.Equal("status is null", process.StartCondition.Expression.Expression);
        Assert.Equal(new TextReference("order.cannotStart"), process.StartCondition.Message);

        Assert.Equal(["check", "big", "small"], process.Steps.Select(step => step.Name));
        var check = Assert.IsType<DecisionStepModel>(process.Steps[0]);
        var branch = Assert.Single(check.Branches);
        Assert.Equal(("NeedsReview(amount)", "big", "small"), (branch.When.Expression, branch.Next, check.Otherwise));
        Assert.Equal(true, Evaluate(branch.When, 10000m));
        Assert.Equal(false, Evaluate(branch.When, 9999.99m));
        Assert.IsType<EndStepModel>(process.Steps[1]);
        Assert.IsType<EndStepModel>(process.Steps[2]);
        Assert.True(process.TryGetStep("BIG", out var big));
        Assert.Equal("big", big.Name);
    }

    [Fact]
    public void Transitions_resolve_ignoring_letter_case_to_the_declared_step_name()
    {
        using var folder = Folder(Process("order", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "amount > 0", "next": "DONE" }], "otherwise": "Done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "CHECK"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        Assert.Equal(("Order", "check"), (process.Entity.Name, process.Start));
        var check = Assert.IsType<DecisionStepModel>(process.Steps[0]);
        Assert.Equal(("done", "done"), (check.Branches[0].Next, check.Otherwise));
    }

    [Fact]
    public void Every_graph_error_of_a_process_is_reported_at_its_path_in_one_pass()
    {
        using var folder = Folder(Process("Nope", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "true", "next": "loopA" }], "otherwise": "missing" },
              { "name": "loopA", "type": "decision", "branches": [{ "when": "true", "next": "loopB" }], "otherwise": "loopB" },
              { "name": "loopB", "type": "decision", "branches": [{ "when": "true", "next": "loopA" }], "otherwise": "loopA" },
              { "name": "orphan", "type": "end" },
              { "name": "CHECK", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        // The duplicate 'CHECK' is not also unreachable, and 'check' leads to an unknown step, so it
        // is not also without an end. Each transition that closes the cycle is reported once.
        Assert.Equal(
            [
                (DiagnosticCodes.UnknownProcessEntity, "/entity", "The entity 'Nope' was not found. No loaded entity has that name."),
                (DiagnosticCodes.UnknownStep, "/steps/0/otherwise", "The step 'missing' was not found. No step of this process has that name."),
                (DiagnosticCodes.StepWithoutEnd, "/steps/1", "The step 'loopA' has no path to an 'end' step."),
                (DiagnosticCodes.StepWithoutEnd, "/steps/2", "The step 'loopB' has no path to an 'end' step."),
                (DiagnosticCodes.ProcessStepCycle, "/steps/2/branches/0/next", "Steps 'loopA' → 'loopB' → 'loopA' form a cycle. A process may not loop."),
                (DiagnosticCodes.UnreachableStep, "/steps/3", "The step 'orphan' cannot be reached from the start step 'check'."),
                (DiagnosticCodes.DuplicateStepName, "/steps/4/name", "The step name 'CHECK' is already used by step 'check' at '/steps/0'."),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.Message)));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(("processes/order-review.json", Guid.Parse(ProcessId)), (diagnostic.File, diagnostic.ResourceId)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_unknown_start_step_is_reported_at_start_and_leaves_no_step_unreachable()
    {
        using var folder = Folder(Process("Order", ReviewSteps, start: "begin"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownStep, "/start"), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_step_that_loops_to_itself_is_a_cycle_at_that_transition()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "amount > 0", "next": "check" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        // The step can still reach the end through 'otherwise', so the cycle is its only problem.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ProcessStepCycle, "/steps/0/branches/0/next", "Steps 'check' → 'check' form a cycle. A process may not loop."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void A_process_over_a_child_entity_is_reported_at_its_entity()
    {
        using var folder = Folder(Process("OrderLine", """[{ "name": "done", "type": "end" }]""", start: "done"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ProcessOverChildEntity, "/entity", "The entity 'OrderLine' is a child entity owned by 'Order.lines'. A process runs for a record of a root entity."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_entity_file_that_was_not_loaded_is_not_reported_again_at_the_process()
    {
        using var folder = Folder(Process("Broken", """[{ "name": "done", "type": "end" }]""", start: "done"))
            .With("entities/broken.json", """{ "id": "11111111-1111-4111-8111-111111111199", "kind": "entity", "name": "Broken", "formatVersion": 1, "fields": [] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "entities/broken.json"), (diagnostic.Code, diagnostic.File));
    }

    [Theory]
    [InlineData(false, "amount", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData(false, "nope > 1", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData(false, "amount >", ExpressionDiagnosticCodes.SyntaxError)]
    [InlineData(true, "amount", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData(true, "nope > 1", ExpressionDiagnosticCodes.UnknownName)]
    // Reference paths are not resolved in a process yet.
    [InlineData(true, "status.name == 'x'", ExpressionDiagnosticCodes.UnknownName)]
    public void A_condition_problem_is_reported_at_that_condition_and_gives_no_model(bool inStartCondition, string expression, string code)
    {
        var steps = $$"""
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "{{(inStartCondition ? "amount > 0" : expression)}}", "next": "done" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """;
        using var folder = Folder(Process("Order", steps, startCondition: inStartCondition ? expression : "status is null"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        var path = inStartCondition ? "/startCondition/expression" : "/steps/0/branches/0/when";
        Assert.Equal((code, "processes/order-review.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_condition_sees_computed_fields_and_child_collections_through_aggregates()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "count(lines) > 0 and large", "next": "done" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void An_unknown_start_condition_text_key_is_reported_at_its_text_key()
    {
        using var folder = Folder(Process("Order", ReviewSteps, startCondition: "status is null", textKey: "order.nope"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "processes/order-review.json", "/startCondition/message/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    private static object? Evaluate(ExpressionModel expression, decimal amount)
    {
        var result = ExpressionInterpreter.Evaluate(
            expression.Syntax, expression.Check, new ExpressionValues([KeyValuePair.Create("amount", (object?)amount)]));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    /// <summary>The process <c>OrderReview</c> over <paramref name="entity"/>, with an optional start condition.</summary>
    private static string Process(
        string entity, string steps, string start = "check", string? startCondition = null, string textKey = "order.cannotStart")
    {
        var condition = startCondition is null
            ? ""
            : $$""" "startCondition": { "expression": "{{startCondition}}", "message": { "textKey": "{{textKey}}" } }, """;
        return $$"""
            { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
              "entity": "{{entity}}", {{condition}} "start": "{{start}}", "steps": {{steps}} }
            """;
    }

    /// <summary>An application with an <c>Order</c> entity that owns <c>OrderLine</c> rows, the rule <c>NeedsReview</c> and <paramref name="process"/>.</summary>
    private static TemporaryFolder Folder(string process) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("rules/needs-review.json", NeedsReview)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "amount", "type": "decimal" },
                    { "name": "status", "type": "text" },
                    { "name": "large", "type": "boolean", "expression": "amount > 1000" },
                    { "name": "lines", "type": "child-collection", "target": "OrderLine" }
                  ] }
                """)
            .With("entities/order-line.json", """
                { "id": "11111111-1111-4111-8111-111111111112", "kind": "entity", "name": "OrderLine", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ] }
                """)
            .With("processes/order-review.json", process);
}
