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
    private const string OrderReviewFormId = "99999999-9999-4999-8999-999999999901";
    private const string PurchasingId = "33333333-3333-4333-8333-333333333301";

    /// <summary>
    /// The connector <c>Purchasing</c>, whose POST operation <c>createOrder</c> takes the required
    /// inputs <c>reference</c> (text) and <c>amount</c> (decimal) and the optional <c>note</c> (text),
    /// and gives the outputs <c>orderNumber</c> (text), <c>flag</c> (boolean) and <c>count</c> (integer).
    /// </summary>
    private const string Purchasing = $$"""
        { "id": "{{PurchasingId}}", "kind": "connector", "name": "Purchasing", "formatVersion": 1,
          "baseUrl": { "binding": "PurchasingUrl" },
          "operations": [
            { "name": "createOrder", "method": "POST", "path": "/orders",
              "input": [
                { "name": "reference", "type": "text", "required": true },
                { "name": "amount", "type": "decimal", "required": true },
                { "name": "note", "type": "text" }
              ],
              "output": [
                { "name": "orderNumber", "type": "text" },
                { "name": "flag", "type": "boolean" },
                { "name": "count", "type": "integer" }
              ] }
          ] }
        """;

    /// <summary>The event <c>OrderPlaced</c> about an <c>Order</c>, with the payload fields <c>amount</c> (decimal) and <c>status</c> (text).</summary>
    private const string OrderPlaced = """
        { "id": "12121212-1212-4212-8212-121212121201", "kind": "event", "name": "OrderPlaced", "formatVersion": 1, "entity": "Order",
          "fields": [ { "name": "amount", "type": "decimal" }, { "name": "status", "type": "text" } ] }
        """;

    private const string CallCreateOrder = """ "connector": { "name": "Purchasing", "operation": "createOrder" }, """;
    private const string CreateOrderInput = """ "input": { "reference": "status", "amount": "amount" }, """;

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": {
            "order.cannotStart": "This order cannot be started.",
            "order.review": "Review the order",
            "role.finance": "Finance",
            "task.approve": "Approve",
            "task.reject": "Reject"
          } }
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
    public void A_process_without_a_subject_compiles_a_decision_over_a_parameter_and_a_rule_call()
    {
        using var folder = Folder(SubjectlessProcess("""[{ "name": "limit", "type": "decimal" }]""", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "NeedsReview(limit)", "next": "big" }], "otherwise": "small" },
              { "name": "big", "type": "end" },
              { "name": "small", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        Assert.Null(process.Entity);
        Assert.Equal([new ProcessParameterModel("limit", FieldType.Decimal)], process.Parameters);
        var check = Assert.IsType<DecisionStepModel>(process.Steps[0]);
        Assert.Equal("NeedsReview(limit)", Assert.Single(check.Branches).When.Expression);
    }

    [Fact]
    public void A_process_without_a_subject_checks_connector_inputs_over_its_parameters()
    {
        using var folder = Folder(
            SubjectlessProcess("""[{ "name": "reference", "type": "text" }, { "name": "limit", "type": "decimal" }]""", $$"""
                [
                  { "name": "call", "type": "operation", "operation": "callConnector", {{CallCreateOrder}}
                    "input": { "reference": "reference", "amount": "limit * 2" }, "next": "done" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var call = Assert.IsType<CallConnectorStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(["reference", "amount"], call.Input.Select(input => input.Input));
        Assert.Empty(call.Output);
    }

    [Fact]
    public void A_process_without_a_subject_sees_no_field_and_no_id()
    {
        using var folder = Folder(SubjectlessProcess("""[{ "name": "limit", "type": "decimal" }]""", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "amount > limit", "next": "big" }], "otherwise": "small" },
              { "name": "big", "type": "end" },
              { "name": "small", "type": "end" }
            ]
            """, startCondition: "id is not null"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [("/startCondition/expression", ExpressionDiagnosticCodes.UnknownName), ("/steps/0/branches/0/when", ExpressionDiagnosticCodes.UnknownName)],
            result.Diagnostics.Select(diagnostic => (diagnostic.Path, diagnostic.Code)));
    }

    [Fact]
    public void A_process_with_both_an_entity_and_parameters_is_reported_at_the_parameters()
    {
        using var folder = Folder($$"""
            { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
              "entity": "Order", "parameters": [{ "name": "limit", "type": "decimal" }], "start": "check", "steps": {{ReviewSteps}} }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidProcessSubject, "/parameters"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_process_with_neither_an_entity_nor_parameters_is_reported_once_at_the_root()
    {
        using var folder = Folder($$"""
            { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
              "start": "check", "steps": {{ReviewSteps}} }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidProcessSubject, ""), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_process_parameter_name_repeated_ignoring_letter_case_is_reported_at_the_repeat()
    {
        using var folder = Folder(SubjectlessProcess(
            """[{ "name": "DailyJob", "type": "text" }, { "name": "limit", "type": "decimal" }, { "name": "dailyjob", "type": "integer" }]""",
            """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "limit > 0", "next": "done" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateProcessParameter, "/parameters/2/name", "The parameter name 'dailyjob' is already used by parameter 'DailyJob' at '/parameters/0'."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void A_task_step_in_a_process_without_a_subject_is_reported_at_its_type_and_not_checked_further()
    {
        using var folder = Folder(SubjectlessProcess(
            """[{ "name": "limit", "type": "decimal" }]""",
            TaskSteps(assignee: """{ "user": "nope" }""", form: "Missing"),
            start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.StepNeedsSubject, "/steps/0/type"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("updateRecord", """ "set": { "status": "'x'" }, """)]
    [InlineData("raiseEvent", """ "event": "OrderPlaced", "fields": {}, """)]
    public void An_update_or_raise_event_step_in_a_process_without_a_subject_is_reported_at_its_operation(string operation, string properties)
    {
        using var folder = Folder(SubjectlessProcess(
            """[{ "name": "limit", "type": "decimal" }]""",
            $$"""
            [
              { "name": "act", "type": "operation", "operation": "{{operation}}", {{properties}} "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """,
            start: "act"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.StepNeedsSubject, "/steps/0/operation"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_connector_output_in_a_process_without_a_subject_is_reported_at_the_output()
    {
        using var folder = Folder(
            SubjectlessProcess("""[{ "name": "reference", "type": "text" }]""", $$"""
                [
                  { "name": "call", "type": "operation", "operation": "callConnector", {{CallCreateOrder}}
                    "input": { "reference": "reference", "amount": "1" }, "output": { "orderNumber": "number" }, "next": "done" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.StepNeedsSubject, "/steps/0/output"), (diagnostic.Code, diagnostic.Path));
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
        Assert.Equal(("Order", "check"), (process.Entity!.Name, process.Start));
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

    [Theory]
    // The fourth '.' is at character 42.
    [InlineData(false, "customer.referredBy.referredBy.referredBy.name == 'x'", ExpressionDiagnosticCodes.TooManyHops, "The path takes 4 hops, at most 3 are allowed at character 42.")]
    [InlineData(true, "customer.referredBy.referredBy.referredBy.name == 'x'", ExpressionDiagnosticCodes.TooManyHops, "The path takes 4 hops, at most 3 are allowed at character 42.")]
    [InlineData(false, "amount.name == 'x'", ExpressionDiagnosticCodes.TypeMismatch, "needs a reference, found decimal")]
    [InlineData(true, "amount.name == 'x'", ExpressionDiagnosticCodes.TypeMismatch, "needs a reference, found decimal")]
    [InlineData(true, "customer.nope == 'x'", ExpressionDiagnosticCodes.UnknownName, "Unknown field 'nope' of 'Customer'")]
    public void A_path_problem_is_reported_at_its_condition(bool inStartCondition, string expression, string code, string messagePart)
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
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_condition_compares_the_id_of_the_subject_with_a_reference_to_it()
    {
        using var folder = Folder(Process("Customer", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "id == referredBy", "next": "done" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_path_cannot_start_at_the_id_of_the_subject()
    {
        using var folder = Folder(Process("Customer", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "id.name == 'x'", "next": "done" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.TypeMismatch, "processes/order-review.json", "/steps/0/branches/0/when"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_condition_follows_paths_through_reference_fields()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "check", "type": "decision", "branches": [{ "when": "customer.referredBy.name == 'x'", "next": "done" }], "otherwise": "done" },
              { "name": "done", "type": "end" }
            ]
            """, startCondition: "customer.REFERREDBY is not null"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        var when = Assert.IsType<DecisionStepModel>(process.Steps[0]).Branches[0].When;
        Assert.Equal(["Customer", "Customer"], when.Check.PathTargets.Values);
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

    [Fact]
    public void An_update_record_operation_compiles_with_its_fields_in_declaration_order()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "submit", "type": "operation", "operation": "updateRecord",
                "set": { "STATUS": "'submitted'", "amount": "amount * 2" }, "next": "Done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "submit"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        var submit = Assert.IsType<OperationStepModel>(process.Steps[0]);
        Assert.Equal(("submit", "updateRecord", "done"), (submit.Name, submit.Operation, submit.Next));
        Assert.Equal(
            [("amount", "amount * 2"), ("status", "'submitted'")],
            submit.Set.Select(assignment => (assignment.Field, assignment.Value.Expression)));
        Assert.Equal(20m, Evaluate(submit.Set[0].Value, 10m));
    }

    [Fact]
    public void A_start_condition_and_a_set_expression_can_use_the_current_time()
    {
        using var folder = Folder(Process(
            "Order",
            """
            [
              { "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "submittedAt": "now()" }, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """,
            start: "submit",
            startCondition: "submittedAt is null or submittedAt < now()"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var submit = Assert.IsType<OperationStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal("date-time", Assert.Single(submit.Set).Value.Check.Type?.ToString());
    }

    [Theory]
    [InlineData("sendEmail", """{ "status": "'x'" }""", DiagnosticCodes.UnknownOperation, "/steps/0/operation", "The operation 'sendEmail' was not found. The built-in operations are 'updateRecord', 'callConnector' and 'raiseEvent'. A package operation is written '<package>/<operation>'.")]
    [InlineData("UpdateRecord", """{ "status": "'x'" }""", DiagnosticCodes.UnknownOperation, "/steps/0/operation", "The operation 'UpdateRecord' was not found. The built-in operations are 'updateRecord', 'callConnector' and 'raiseEvent'. A package operation is written '<package>/<operation>'.")]
    [InlineData("updateRecord", """{ "nope": "'x'" }""", DiagnosticCodes.UnknownSetField, "/steps/0/set/nope", "The field 'nope' was not found. The entity 'Order' has no field with that name.")]
    [InlineData("updateRecord", """{ "status": "'x'", "STATUS": "'y'" }""", DiagnosticCodes.UnknownSetField, "/steps/0/set/STATUS", "The field 'status' is already set by 'status' in this step.")]
    [InlineData("updateRecord", """{ "large": "true" }""", DiagnosticCodes.ReadOnlySetField, "/steps/0/set/large", "The field 'large' is computed, so an operation cannot set it.")]
    [InlineData("updateRecord", """{ "number": "'x'" }""", DiagnosticCodes.ReadOnlySetField, "/steps/0/set/number", "The field 'number' is numbered by a sequence, so an operation cannot set it.")]
    [InlineData("updateRecord", """{ "lines": "null" }""", DiagnosticCodes.ReadOnlySetField, "/steps/0/set/lines", "The field 'lines' is a child collection, so an operation cannot set it.")]
    [InlineData("updateRecord", """{ "amount": "'many'" }""", ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/set/amount", "The expression must be decimal, but it is text.")]
    public void An_operation_problem_is_reported_at_its_path_in_the_step_and_gives_no_model(
        string operation, string set, string code, string path, string message)
    {
        using var folder = Folder(Process("Order", $$"""
            [
              { "name": "submit", "type": "operation", "operation": "{{operation}}", "set": {{set}}, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "submit"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "processes/order-review.json", path, message), (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("customer.nope", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("amount +", ExpressionDiagnosticCodes.SyntaxError)]
    public void A_set_expression_gets_the_usual_expression_diagnostics_at_its_field(string expression, string code)
    {
        using var folder = Folder(Process("Order", $$"""
            [
              { "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "{{expression}}" }, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "submit"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "/steps/0/set/status"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_connector_call_compiles_with_declared_names_inputs_in_operation_order_and_the_retry_defaults()
    {
        using var folder = Folder(
            Process("Order", """
                [
                  { "name": "call", "type": "operation", "operation": "callConnector",
                    "connector": { "name": "PURCHASING", "operation": "CreateOrder" },
                    "input": { "Note": "'urgent'", "AMOUNT": "amount * 2", "reference": "status" },
                    "output": { "COUNT": "Amount", "orderNumber": "STATUS" },
                    "next": "DONE" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var call = Assert.IsType<CallConnectorStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(("call", "Purchasing", "createOrder", "done"), (call.Name, call.Connector, call.Operation, call.Next));
        Assert.Equal(
            [("reference", "status"), ("amount", "amount * 2"), ("note", "'urgent'")],
            call.Input.Select(input => (input.Input, input.Value.Expression)));
        Assert.Equal(20m, Evaluate(call.Input[1].Value, 10m));
        Assert.Equal([new ConnectorOutputModel("orderNumber", "status"), new ConnectorOutputModel("count", "amount")], call.Output);
        Assert.Equal(new RetryPolicyModel(3, TimeSpan.FromSeconds(30)), call.Retry);
    }

    [Fact]
    public void A_connector_call_keeps_its_written_retry_policy_and_may_have_no_inputs_or_outputs()
    {
        var connector = Purchasing.Replace(
            """{ "name": "reference", "type": "text", "required": true },""", "").Replace(
            """{ "name": "amount", "type": "decimal", "required": true },""", "");
        using var folder = Folder(
            Process("Order", $$"""
                [
                  { "name": "call", "type": "operation", "operation": "callConnector", {{CallCreateOrder}}
                    "input": {}, "retry": { "attempts": 10, "backoff": "PT1M" }, "next": "done" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            connector);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var call = Assert.IsType<CallConnectorStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Empty(call.Input);
        Assert.Empty(call.Output);
        Assert.Equal(new RetryPolicyModel(10, TimeSpan.FromMinutes(1)), call.Retry);
    }

    [Theory]
    [InlineData(
        """ "connector": { "name": "Billing", "operation": "createOrder" }, """ + CreateOrderInput,
        DiagnosticCodes.UnknownConnector, "/steps/0/connector/name", "The connector 'Billing' was not found. No loaded connector has that name.")]
    [InlineData(
        """ "connector": { "name": "Purchasing", "operation": "cancelOrder" }, """ + CreateOrderInput,
        DiagnosticCodes.UnknownConnectorOperation, "/steps/0/connector/operation", "The operation 'cancelOrder' was not found. The connector 'Purchasing' has no operation with that name.")]
    [InlineData(
        CallCreateOrder + """ "input": { "reference": "status", "amount": "'many'" }, """,
        ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/input/amount", "The expression must be decimal, but it is text.")]
    [InlineData(
        CallCreateOrder + """ "input": { "reference": "status", "amount": "amount", "nope": "1" }, """,
        DiagnosticCodes.UnknownConnectorInput, "/steps/0/input/nope", "The input 'nope' was not found. The operation 'createOrder' has no input with that name.")]
    [InlineData(
        CallCreateOrder + """ "input": { "reference": "status", "amount": "amount", "AMOUNT": "1" }, """,
        DiagnosticCodes.UnknownConnectorInput, "/steps/0/input/AMOUNT", "The input 'amount' is already given by 'amount' in this step.")]
    [InlineData(
        CallCreateOrder + """ "input": { "amount": "amount" }, """,
        DiagnosticCodes.MissingConnectorInput, "/steps/0/input", "The input 'reference' is required by the operation 'createOrder', but the step does not give it.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "nope": "status" }, """,
        DiagnosticCodes.UnknownConnectorOutput, "/steps/0/output/nope", "The output 'nope' was not found. The operation 'createOrder' has no output with that name.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "orderNumber": "status", "ORDERNUMBER": "submittedAt" }, """,
        DiagnosticCodes.UnknownConnectorOutput, "/steps/0/output/ORDERNUMBER", "The output 'orderNumber' is already written by 'orderNumber' in this step.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "orderNumber": "nope" }, """,
        DiagnosticCodes.UnknownOutputField, "/steps/0/output/orderNumber", "The field 'nope' was not found. The entity 'Order' has no field with that name.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "count": "amount", "orderNumber": "AMOUNT" }, """,
        DiagnosticCodes.UnknownOutputField, "/steps/0/output/orderNumber", "The field 'amount' is already written by the output 'count' in this step.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "flag": "large" }, """,
        DiagnosticCodes.InvalidOutputField, "/steps/0/output/flag", "The field 'large' is computed, so an output cannot write it.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "orderNumber": "number" }, """,
        DiagnosticCodes.InvalidOutputField, "/steps/0/output/orderNumber", "The field 'number' is numbered by a sequence, so an output cannot write it.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "count": "lines" }, """,
        DiagnosticCodes.InvalidOutputField, "/steps/0/output/count", "The field 'lines' is a child collection, so an output cannot write it.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "flag": "status" }, """,
        DiagnosticCodes.InvalidOutputField, "/steps/0/output/flag", "The output 'flag' is boolean, so it cannot be written to the field 'status', which is text.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "output": { "orderNumber": "customer" }, """,
        DiagnosticCodes.InvalidOutputField, "/steps/0/output/orderNumber", "The output 'orderNumber' is text, so it cannot be written to the field 'customer', which is reference.")]
    [InlineData(
        CallCreateOrder + CreateOrderInput + """ "retry": { "backoff": "P1M" }, """,
        DiagnosticCodes.InvalidRetryBackoff, "/steps/0/retry/backoff", "The duration 'P1M' is not valid. 'backoff' is a positive ISO 8601 duration in whole numbers: weeks, such as 'P2W', or days with an optional time part, such as 'P1D', 'PT30S' or 'P1DT12H'. Years and months are not allowed.")]
    public void A_connector_call_problem_is_reported_at_its_path_in_the_step_and_gives_no_model(
        string properties, string code, string path, string message)
    {
        using var folder = Folder(
            Process("Order", $$"""
                [
                  { "name": "call", "type": "operation", "operation": "callConnector", {{properties}} "next": "done" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "processes/order-review.json", path, message), (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_connector_call_over_an_unknown_entity_still_checks_its_connector_and_retry()
    {
        using var folder = Folder(
            Process("Nope", $$"""
                [
                  { "name": "call", "type": "operation", "operation": "callConnector",
                    "connector": { "name": "Billing", "operation": "createOrder" },
                    "input": { "anything": "nope" }, "output": { "anything": "nope" }, "retry": { "backoff": "soon" }, "next": "done" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownProcessEntity, "/entity"),
                (DiagnosticCodes.UnknownConnector, "/steps/0/connector/name"),
                (DiagnosticCodes.InvalidRetryBackoff, "/steps/0/retry/backoff"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void A_connector_whose_file_was_not_loaded_is_not_reported_again_by_a_call()
    {
        using var folder = Folder(
            Process("Order", $$"""
                [
                  { "name": "call", "type": "operation", "operation": "callConnector", {{CallCreateOrder}} {{CreateOrderInput}} "next": "done" },
                  { "name": "done", "type": "end" }
                ]
                """, start: "call"),
            Purchasing.Replace("\"method\": \"POST\"", "\"method\": \"SEND\""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "connectors/purchasing.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData(
        """{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "set": { "status": "'x'" }, "next": "done" }""",
        "/steps/0/set")]
    [InlineData(
        """{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "connector": { "name": "Purchasing", "operation": "createOrder" }, "next": "done" }""",
        "/steps/0/connector")]
    public void A_set_on_a_connector_call_and_a_connector_on_an_update_are_schema_violations(string step, string path)
    {
        using var folder = Folder(Process("Order", $$"""[{{step}}, { "name": "done", "type": "end" }]""", start: "submit"), Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "processes/order-review.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_raised_event_compiles_with_its_declared_name_and_its_payload_in_event_order()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "orderPLACED",
                "fields": { "STATUS": "status", "Amount": "amount * 2" }, "next": "DONE" },
              { "name": "done", "type": "end" }
            ]
            """, start: "announce"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var raise = Assert.IsType<RaiseEventStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(("announce", "OrderPlaced", "done"), (raise.Name, raise.Event, raise.Next));
        Assert.Equal(
            [("amount", "amount * 2"), ("status", "status")],
            raise.Fields.Select(field => (field.Field, field.Value.Expression)));
        Assert.Equal(20m, Evaluate(raise.Fields[0].Value, 10m));
        Assert.Equal("text", raise.Fields[1].Value.Check.Type?.ToString());
    }

    [Theory]
    [InlineData(
        "Nope", """{ "amount": "amount", "status": "status" }""",
        DiagnosticCodes.UnknownStepEvent, "/steps/0/event", "The event 'Nope' was not found. No loaded event has that name.")]
    [InlineData(
        "CustomerJoined", """{ "name": "status" }""",
        DiagnosticCodes.StepEventOverOtherEntity, "/steps/0/event", "The event 'CustomerJoined' is about the entity 'Customer'. A step can raise only an event about the process's entity 'Order'.")]
    [InlineData(
        "OrderPlaced", """{ "amount": "'many'", "status": "status" }""",
        ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/fields/amount", "The expression must be decimal, but it is text.")]
    [InlineData(
        "OrderPlaced", """{ "amount": "amount", "status": "status", "nope": "1" }""",
        DiagnosticCodes.UnknownEventPayloadField, "/steps/0/fields/nope", "The field 'nope' was not found. The event 'OrderPlaced' has no payload field with that name.")]
    [InlineData(
        "OrderPlaced", """{ "amount": "amount", "status": "status", "AMOUNT": "1" }""",
        DiagnosticCodes.UnknownEventPayloadField, "/steps/0/fields/AMOUNT", "The field 'amount' is already given by 'amount' in this step.")]
    [InlineData(
        "OrderPlaced", """{ "amount": "amount" }""",
        DiagnosticCodes.MissingEventPayloadField, "/steps/0/fields", "The field 'status' is a payload field of the event 'OrderPlaced', but the step does not give it.")]
    public void A_raised_event_problem_is_reported_at_its_path_in_the_step_and_gives_no_model(
        string @event, string fields, string code, string path, string message)
    {
        using var folder = Folder(Process("Order", $$"""
            [
              { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "{{@event}}", "fields": {{fields}}, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "announce"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "processes/order-review.json", path, message), (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("customer.nope", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("status +", ExpressionDiagnosticCodes.SyntaxError)]
    public void A_payload_expression_gets_the_usual_expression_diagnostics_at_its_field(string expression, string code)
    {
        using var folder = Folder(Process("Order", $$"""
            [
              { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced",
                "fields": { "amount": "amount", "status": "{{expression}}" }, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "announce"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "/steps/0/fields/status"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_raised_event_over_an_unknown_entity_still_checks_its_event_and_payload_keys()
    {
        using var folder = Folder(Process("Nope", """
            [
              { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced",
                "fields": { "amount": "nope", "nope": "nope" }, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "announce"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownProcessEntity, "/entity"),
                (DiagnosticCodes.MissingEventPayloadField, "/steps/0/fields"),
                (DiagnosticCodes.UnknownEventPayloadField, "/steps/0/fields/nope"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void An_event_whose_file_was_not_loaded_is_not_reported_again_by_a_raise()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "OrderShipped", "fields": {}, "next": "done" },
              { "name": "done", "type": "end" }
            ]
            """, start: "announce"))
            .With("events/order-shipped.json", """{ "id": "12121212-1212-4212-8212-121212121209", "kind": "event", "name": "OrderShipped", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "events/order-shipped.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData(
        """{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "fields": {}, "set": { "status": "'x'" }, "next": "done" }""",
        "/steps/0/set")]
    [InlineData(
        """{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "fields": {}, "input": {}, "next": "done" }""",
        "/steps/0/input")]
    [InlineData(
        """{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "event": "OrderPlaced", "next": "done" }""",
        "/steps/0/event")]
    [InlineData(
        """{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "fields": {}, "next": "done" }""",
        "/steps/0/fields")]
    public void A_property_of_another_operation_on_a_raise_and_an_event_on_another_operation_are_schema_violations(string step, string path)
    {
        using var folder = Folder(Process("Order", $$"""[{{step}}, { "name": "done", "type": "end" }]""", start: "submit"), Purchasing);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "processes/order-review.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_operation_next_joins_the_step_graph_checks()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "next": "missing" },
              { "name": "loop", "type": "operation", "operation": "updateRecord", "set": { "status": "'y'" }, "next": "loop" },
              { "name": "done", "type": "end" }
            ]
            """, start: "submit"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownStep, "/steps/0/next"),
                (DiagnosticCodes.UnreachableStep, "/steps/1"),
                (DiagnosticCodes.StepWithoutEnd, "/steps/1"),
                (DiagnosticCodes.ProcessStepCycle, "/steps/1/next"),
                (DiagnosticCodes.UnreachableStep, "/steps/2"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" } }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "set": { "status": "'x'" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": {}, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "next": "done", "otherwise": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "decision", "branches": [{ "when": "true", "next": "done" }], "otherwise": "done", "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "end", "set": { "status": "'x'" } }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview" }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview", "outcomes": [] }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }], "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }] }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "queue": "x" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }] }""")]
    [InlineData("""{ "name": "submit", "type": "task", "assignee": { "role": "finance" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }] }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "next": "done" }] }""")]
    [InlineData("""{ "name": "submit", "type": "decision", "branches": [{ "when": "true", "next": "done" }], "otherwise": "done", "form": "OrderReview" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "next": "done", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }] }""")]
    [InlineData("""{ "name": "submit", "type": "end", "dueIn": "P3D" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "input": { "amount": "1" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "output": { "count": "amount" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "updateRecord", "set": { "status": "'x'" }, "retry": { "attempts": 2 }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "input": {}, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing" }, "input": {}, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder", "version": "2" }, "input": {}, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": { "amount": "" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "output": { "count": "not a name" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "retry": { "attempts": 0 }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "retry": { "attempts": 11 }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "retry": { "attempts": 2.5 }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "retry": { "backoff": "" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "callConnector", "connector": { "name": "Purchasing", "operation": "createOrder" }, "input": {}, "retry": { "delay": "PT1S" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "decision", "branches": [{ "when": "true", "next": "done" }], "otherwise": "done", "retry": { "attempts": 2 } }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }], "input": {} }""")]
    [InlineData("""{ "name": "submit", "type": "end", "connector": { "name": "Purchasing", "operation": "createOrder" } }""")]
    [InlineData("""{ "name": "submit", "type": "end", "output": {} }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "raiseEvent", "fields": {}, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "fields": { "amount": "" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "fields": { "not a name": "1" }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "fields": {}, "retry": { "attempts": 2 }, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "operation", "operation": "raiseEvent", "event": "OrderPlaced", "fields": {}, "output": {}, "next": "done" }""")]
    [InlineData("""{ "name": "submit", "type": "decision", "branches": [{ "when": "true", "next": "done" }], "otherwise": "done", "event": "OrderPlaced" }""")]
    [InlineData("""{ "name": "submit", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview", "outcomes": [{ "name": "ok", "label": { "textKey": "task.approve" }, "next": "done" }], "fields": {} }""")]
    [InlineData("""{ "name": "submit", "type": "end", "event": "OrderPlaced" }""")]
    public void A_step_shape_the_schema_refuses_is_a_schema_violation(string step)
    {
        using var folder = Folder(Process("Order", $$"""[{{step}}, { "name": "done", "type": "end" }]""", start: "submit"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.File == "processes/order-review.json");
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_task_step_compiles_into_the_model_with_its_assignee_form_due_duration_and_outcomes()
    {
        using var folder = Folder(Process("Order", TaskSteps(), start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        var review = Assert.IsType<TaskStepModel>(process.Steps[0]);
        Assert.Equal(
            ("review", new TextReference("order.review"), new FormReference(Guid.Parse(OrderReviewFormId), "OrderReview"), (TimeSpan?)TimeSpan.FromDays(3)),
            (review.Name, review.Label, review.Form, review.DueIn));
        Assert.Null(review.Assignee.Role);
        Assert.NotNull(review.Assignee.User);
        Assert.Equal("'u1'", review.Assignee.User.Expression);
        Assert.Equal("u1", Evaluate(review.Assignee.User, 0m));
        Assert.Equal(
            [("approve", new TextReference("task.approve"), "approved"), ("reject", new TextReference("task.reject"), "rejected")],
            review.Outcomes.Select(outcome => (outcome.Name, outcome.Label, outcome.Next)));
    }

    [Fact]
    public void A_task_step_assigned_to_a_role_with_no_due_date_compiles()
    {
        using var folder = Folder(Process("Order", TaskSteps(assignee: """{ "role": "finance" }""", dueIn: null), start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var review = Assert.IsType<TaskStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal((null, "finance", null), (review.Assignee.User, review.Assignee.Role, review.DueIn));
    }

    [Theory]
    [InlineData("""{ "user": "'u1'", "role": "finance" }""", "OrderReview", "P3D", "reject", DiagnosticCodes.InvalidTaskAssignee, "/steps/0/assignee", "A task's assignee must have exactly one of 'user' and 'role'.")]
    [InlineData("{}", "OrderReview", "P3D", "reject", DiagnosticCodes.InvalidTaskAssignee, "/steps/0/assignee", "A task's assignee must have exactly one of 'user' and 'role'.")]
    [InlineData("""{ "role": "finance" }""", "Nope", "P3D", "reject", DiagnosticCodes.UnknownTaskForm, "/steps/0/form", "The form 'Nope' was not found. No loaded form has that name.")]
    [InlineData("""{ "role": "finance" }""", "CustomerCard", "P3D", "reject", DiagnosticCodes.TaskFormOverOtherEntity, "/steps/0/form", "The form 'CustomerCard' is over the entity 'Customer'. A task's form must be over the process's entity 'Order'.")]
    [InlineData("""{ "role": "finance" }""", "OrderReview", "P0D", "reject", DiagnosticCodes.InvalidTaskDueIn, "/steps/0/dueIn", null)]
    [InlineData("""{ "role": "finance" }""", "OrderReview", "P1M", "reject", DiagnosticCodes.InvalidTaskDueIn, "/steps/0/dueIn", null)]
    [InlineData("""{ "role": "finance" }""", "OrderReview", "soon", "reject", DiagnosticCodes.InvalidTaskDueIn, "/steps/0/dueIn", null)]
    [InlineData("""{ "role": "finance" }""", "OrderReview", "P3D", "APPROVE", DiagnosticCodes.DuplicateOutcomeName, "/steps/0/outcomes/1/name", "The outcome name 'APPROVE' is already used by outcome 'approve' at '/steps/0/outcomes/0'.")]
    public void A_task_problem_is_reported_with_its_own_code_at_its_path_and_gives_no_model(
        string assignee, string form, string dueIn, string secondOutcome, string code, string path, string? message)
    {
        var steps = TaskSteps(assignee: assignee, form: form, dueIn: dueIn, secondOutcome: secondOutcome);
        using var folder = Folder(Process("Order", steps, start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "processes/order-review.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        if (message is not null)
        {
            Assert.Equal(message, diagnostic.Message);
        }
        else
        {
            Assert.Contains($"'{dueIn}'", diagnostic.Message, StringComparison.Ordinal);
        }

        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("amount", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("customer.nope", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("status +", ExpressionDiagnosticCodes.SyntaxError)]
    public void A_user_assignee_expression_gets_the_usual_expression_diagnostics_at_its_user(string expression, string code)
    {
        using var folder = Folder(Process("Order", TaskSteps(assignee: $$"""{ "user": "{{expression}}" }"""), start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "/steps/0/assignee/user"), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_user_assignee_follows_paths_through_reference_fields()
    {
        using var folder = Folder(Process("Order", TaskSteps(assignee: """{ "user": "customer.referredBy.name" }"""), start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_user_assignee_can_use_the_current_time()
    {
        using var folder = Folder(Process(
            "Order",
            TaskSteps(assignee: """{ "user": "if(now() > dateTime('2000-01-01T00:00:00Z'), 'u1', 'u2')" }"""),
            start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Missing_task_and_outcome_label_keys_are_reported_at_their_text_keys()
    {
        var steps = TaskSteps().Replace("\"order.review\"", "\"order.nope\"", StringComparison.Ordinal)
            .Replace("\"task.reject\"", "\"task.nope\"", StringComparison.Ordinal);
        using var folder = Folder(Process("Order", steps, start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.MissingTextKey, "/steps/0/label/textKey", "No locale has the text key 'order.nope'."),
                (DiagnosticCodes.MissingTextKey, "/steps/0/outcomes/1/label/textKey", "No locale has the text key 'task.nope'."),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.Message)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_outcome_next_joins_the_step_graph_checks()
    {
        using var folder = Folder(Process("Order", """
            [
              { "name": "review", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview",
                "outcomes": [
                  { "name": "approve", "label": { "textKey": "task.approve" }, "next": "done" },
                  { "name": "reject", "label": { "textKey": "task.reject" }, "next": "missing" }
                ] },
              { "name": "again", "type": "task", "label": { "textKey": "order.review" }, "assignee": { "role": "finance" }, "form": "OrderReview",
                "outcomes": [{ "name": "retry", "label": { "textKey": "task.reject" }, "next": "again" }] },
              { "name": "done", "type": "end" }
            ]
            """, start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownStep, "/steps/0/outcomes/1/next"),
                (DiagnosticCodes.UnreachableStep, "/steps/1"),
                (DiagnosticCodes.StepWithoutEnd, "/steps/1"),
                (DiagnosticCodes.ProcessStepCycle, "/steps/1/outcomes/0/next"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_task_form_whose_file_was_not_loaded_is_not_reported_again_at_the_step()
    {
        using var folder = Folder(Process("Order", TaskSteps(form: "BrokenForm"), start: "review"))
            .With("forms/broken-form.json", """{ "id": "99999999-9999-4999-8999-999999999909", "kind": "form", "name": "BrokenForm", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "forms/broken-form.json"), (diagnostic.Code, diagnostic.File));
    }

    [Theory]
    [InlineData("auditor")]
    [InlineData("FINANCE")]
    public void A_task_role_that_names_no_declared_role_exactly_is_reported_at_the_assignee_role(string role)
    {
        using var folder = Folder(Process("Order", TaskSteps(assignee: $$"""{ "role": "{{role}}" }"""), start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTaskRole, "processes/order-review.json", "/steps/0/assignee/role", $"The role '{role}' was not found. No declared role has that name."),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_task_role_whose_file_was_not_loaded_is_not_reported_again_at_the_step()
    {
        using var folder = Folder(Process("Order", TaskSteps(assignee: """{ "role": "auditor" }"""), start: "review"))
            .With("roles/auditor.json", """{ "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "auditor", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "roles/auditor.json"), (diagnostic.Code, diagnostic.File));
    }

    [Fact]
    public void A_task_over_an_unknown_entity_checks_neither_its_user_expression_nor_its_form_entity()
    {
        using var folder = Folder(Process("Nope", TaskSteps(assignee: """{ "user": "amount" }""", form: "CustomerCard"), start: "review"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownProcessEntity, "/entity"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("P2W", 14 * 24 * 60 * 60)]
    [InlineData("P3D", 3 * 24 * 60 * 60)]
    [InlineData("PT4H", 4 * 60 * 60)]
    [InlineData("P1DT12H", 36 * 60 * 60)]
    [InlineData("PT1H30M15S", (60 * 60) + (30 * 60) + 15)]
    [InlineData("P0DT1S", 1)]
    public void A_positive_whole_number_duration_in_weeks_or_days_and_time_parses(string text, int seconds)
    {
        Assert.True(IsoDuration.TryParse(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("PT")]
    [InlineData("P1DT")]
    [InlineData("P1M")]
    [InlineData("P1Y")]
    [InlineData("P0D")]
    [InlineData("PT0S")]
    [InlineData("-P1D")]
    [InlineData("P1.5D")]
    [InlineData("P1W2D")]
    [InlineData("p1d")]
    [InlineData("P1D ")]
    [InlineData("P1D\n")]
    [InlineData("P٣D")]
    [InlineData("P99999999999999999999D")]
    [InlineData("P9999999999999D")]
    public void A_duration_outside_the_accepted_form_or_not_positive_does_not_parse(string text)
    {
        Assert.False(IsoDuration.TryParse(text, out var duration));
        Assert.Equal(TimeSpan.Zero, duration);
    }

    private static object? Evaluate(ExpressionModel expression, decimal amount)
    {
        var result = ExpressionInterpreter.Evaluate(
            expression.Syntax, expression.Check, new ExpressionValues([KeyValuePair.Create("amount", (object?)amount)]));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    /// <summary>
    /// Steps that start with the task <c>review</c> on <paramref name="form"/>, whose outcomes
    /// <c>approve</c> and <paramref name="secondOutcome"/> lead to the end steps <c>approved</c> and <c>rejected</c>.
    /// </summary>
    private static string TaskSteps(
        string assignee = """{ "user": "'u1'" }""",
        string form = "OrderReview",
        string? dueIn = "P3D",
        string secondOutcome = "reject")
    {
        var due = dueIn is null ? "" : $$""" "dueIn": "{{dueIn}}", """;
        return $$"""
            [
              { "name": "review", "type": "task", "label": { "textKey": "order.review" }, "assignee": {{assignee}}, "form": "{{form}}", {{due}}
                "outcomes": [
                  { "name": "approve", "label": { "textKey": "task.approve" }, "next": "approved" },
                  { "name": "{{secondOutcome}}", "label": { "textKey": "task.reject" }, "next": "rejected" }
                ] },
              { "name": "approved", "type": "end" },
              { "name": "rejected", "type": "end" }
            ]
            """;
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

    /// <summary>The process <c>OrderReview</c> with no entity and the given <paramref name="parameters"/> array, with an optional start condition.</summary>
    private static string SubjectlessProcess(string parameters, string steps, string start = "check", string? startCondition = null)
    {
        var condition = startCondition is null
            ? ""
            : $$""" "startCondition": { "expression": "{{startCondition}}", "message": { "textKey": "order.cannotStart" } }, """;
        return $$"""
            { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
              "parameters": {{parameters}}, {{condition}} "start": "{{start}}", "steps": {{steps}} }
            """;
    }

    /// <summary>
    /// An application with an <c>Order</c> entity that references a <c>Customer</c>, owns <c>OrderLine</c> rows and
    /// takes its number from the <c>OrderNumber</c> sequence, a <c>Customer</c> that references the customer who
    /// referred it, the rule <c>NeedsReview</c>, the forms <c>OrderReview</c> over <c>Order</c> and <c>CustomerCard</c>
    /// over <c>Customer</c>, the role <c>finance</c>, the events <c>OrderPlaced</c> about <c>Order</c> and
    /// <c>CustomerJoined</c> about <c>Customer</c>, <paramref name="process"/>, and
    /// <paramref name="connector"/> as <c>connectors/purchasing.json</c> when it is given.
    /// </summary>
    private static TemporaryFolder Folder(string process, string? connector = null)
    {
        var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("rules/needs-review.json", NeedsReview)
            .With("roles/finance.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "finance", "formatVersion": 1, "label": { "textKey": "role.finance" } }
                """)
            .With("sequences/order-number.json", """
                { "id": "88888888-8888-4888-8888-888888888801", "kind": "sequence", "name": "OrderNumber", "formatVersion": 1, "format": "O-{n:5}" }
                """)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "amount", "type": "decimal" },
                    { "name": "status", "type": "text" },
                    { "name": "submittedAt", "type": "date-time" },
                    { "name": "customer", "type": "reference", "target": "Customer" },
                    { "name": "large", "type": "boolean", "expression": "amount > 1000" },
                    { "name": "lines", "type": "child-collection", "target": "OrderLine" },
                    { "name": "number", "type": "text", "sequence": "OrderNumber" }
                  ] }
                """)
            .With("entities/order-line.json", """
                { "id": "11111111-1111-4111-8111-111111111112", "kind": "entity", "name": "OrderLine", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ] }
                """)
            .With("entities/customer.json", """
                { "id": "11111111-1111-4111-8111-111111111113", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [
                    { "name": "name", "type": "text", "required": true },
                    { "name": "referredBy", "type": "reference", "target": "Customer" }
                  ] }
                """)
            .With("forms/order-review.json", $$"""
                { "id": "{{OrderReviewFormId}}", "kind": "form", "name": "OrderReview", "formatVersion": 1, "entity": "Order",
                  "sections": [ { "title": { "textKey": "order.review" }, "fields": [ { "field": "amount" } ] } ] }
                """)
            .With("forms/customer-card.json", """
                { "id": "99999999-9999-4999-8999-999999999902", "kind": "form", "name": "CustomerCard", "formatVersion": 1, "entity": "Customer",
                  "sections": [ { "title": { "textKey": "order.review" }, "fields": [ { "field": "name" } ] } ] }
                """)
            .With("events/order-placed.json", OrderPlaced)
            .With("events/customer-joined.json", """
                { "id": "12121212-1212-4212-8212-121212121202", "kind": "event", "name": "CustomerJoined", "formatVersion": 1, "entity": "Customer",
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("processes/order-review.json", process);
        return connector is null ? folder : folder.With("connectors/purchasing.json", connector);
    }
}
