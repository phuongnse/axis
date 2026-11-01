using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ModuleContractTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e11";
    private const string ContractId = "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b71";
    private const string SecondContractId = "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b72";
    private const string SeedId = "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b73";
    private const string ContractFile = "modules/approvals/Contract.json";
    private const string ScenarioPath = "scenarios/Covers.json";

    private const string DefaultStates = """
        [ { "name": "draft", "initial": true, "deletable": true }, { "name": "submitted" }, { "name": "approved" } ]
        """;

    private const string DefaultTransitions = """
        [
          { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "IsSmall(amount)",
            "message": { "textKey": "approvals.request.small" } },
          { "name": "approve", "from": ["submitted"], "to": "approved", "guard": "amount > 0" }
        ]
        """;

    [Fact]
    public void A_valid_contract_compiles_and_the_models_carry_it_on_the_entity()
    {
        using var modules = Modules(Contract(Lifecycle()));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var request = Assert.Single(model.Entities, entity => entity.Name == "approvals/Request");
            var contract = Assert.IsType<ContractModel>(request.Contract);
            Assert.Equal(("approvals/RequestContract", ContractFile), (contract.Name, contract.File));

            var lifecycle = Assert.IsType<LifecycleModel>(contract.Lifecycle);
            Assert.Equal("status", lifecycle.Field);
            Assert.Equal(
                [("draft", true, true), ("submitted", false, false), ("approved", false, false)],
                lifecycle.States.Select(state => (state.Name, state.Initial, state.Deletable)));
            Assert.Equal(["submit", "approve"], lifecycle.Transitions.Select(transition => transition.Name));
            Assert.Equal(["draft"], lifecycle.Transitions[0].From);
            Assert.Equal("submitted", lifecycle.Transitions[0].To);
            Assert.Equal("IsSmall(amount)", lifecycle.Transitions[0].Guard!.Expression);
            Assert.Equal("approvals.request.small", lifecycle.Transitions[0].Message!.TextKey);
            Assert.Null(lifecycle.Transitions[1].Message);

            Assert.Null(Assert.Single(model.Entities, entity => entity.Name == "approvals/Line").Contract);
        }
    }

    [Fact]
    public void A_module_seed_record_in_a_state_that_is_not_initial_is_axc0336()
    {
        using var modules = Modules(Contract(Lifecycle()), ("Seed.json", Seed("submitted")));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SeedStateNotInitial, "modules/approvals/Seed.json", "/records/0/values/status", (Guid?)Guid.Parse(SeedId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'submitted'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("draft", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_seed_record_in_an_initial_state_compiles()
    {
        using var modules = Modules(Contract(Lifecycle()), ("Seed.json", Seed("draft")));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Single(compiled.Model!.Seeds);
    }

    [Fact]
    public void A_contract_with_only_a_lifecycle_of_states_compiles()
    {
        using var modules = Modules(Contract(Lifecycle(transitions: "[]")));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var lifecycle = compiled.Model!.Entities.Single(entity => entity.Name == "approvals/Request").Contract!.Lifecycle!;
        Assert.Empty(lifecycle.Transitions);
    }

    [Fact]
    public void A_contract_file_in_an_application_folder_is_axc0320_at_the_kind_and_nothing_else()
    {
        using var modules = Modules();
        using var folder = Application().With("contracts/Request.json", Contract(Lifecycle()));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ContractOutsideModule, "contracts/Request.json", "/kind", Guid.Parse(ContractId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("module", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("approvals/Request")]
    public void A_contract_for_an_unknown_entity_or_a_name_with_a_slash_is_axc0321(string entity)
    {
        var diagnostic = Single(Contract(Lifecycle(), entity));

        AssertDiagnostic(DiagnosticCodes.UnknownContractEntity, "/entity", diagnostic);
        Assert.Contains($"'{entity}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_for_a_child_entity_is_axc0322_and_names_the_owner()
    {
        var diagnostic = Single(Contract(Lifecycle(), "Line"));

        AssertDiagnostic(DiagnosticCodes.ContractOnChildEntity, "/entity", diagnostic);
        Assert.Contains("approvals/Request.lines", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_contract_for_an_entity_is_axc0323_on_the_later_file_and_names_the_first()
    {
        using var modules = Modules(
            Contract(Lifecycle()),
            ("Contract2.json", Contract(null, name: "SecondContract", id: SecondContractId, extra: """, "ceiling": { "others": [{ "actions": ["read"] }] }""")));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateContract, "modules/approvals/Contract2.json", "/entity", Guid.Parse(SecondContractId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains(ContractFile, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_without_any_part_is_axc0324_at_the_root()
    {
        var diagnostic = Single(Contract(null));

        AssertDiagnostic(DiagnosticCodes.EmptyContract, "", diagnostic);
    }

    [Theory]
    [InlineData("missing", "names no field")]
    [InlineData("title", "not an enum")]
    [InlineData("stage", "not required")]
    [InlineData("locked", "is sensitive")]
    public void A_state_field_that_is_not_a_required_plain_enum_is_axc0325(string field, string reason)
    {
        var diagnostic = Single(Contract(Lifecycle(field: field)));

        AssertDiagnostic(DiagnosticCodes.InvalidLifecycleField, "/lifecycle/field", diagnostic);
        Assert.Contains(reason, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_that_is_not_a_value_of_the_field_is_axc0326_at_the_state()
    {
        var states = """
            [ { "name": "draft", "initial": true }, { "name": "submitted" }, { "name": "approved" }, { "name": "archived" } ]
            """;

        var diagnostic = Single(Contract(Lifecycle(states: states)));

        AssertDiagnostic(DiagnosticCodes.LifecycleStatesMismatch, "/lifecycle/states/3/name", diagnostic);
        Assert.Contains("'archived'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_of_the_field_without_a_state_is_axc0326_at_the_states()
    {
        var states = """[ { "name": "draft", "initial": true }, { "name": "submitted" } ]""";
        var transitions = """[ { "name": "submit", "from": ["draft"], "to": "submitted" } ]""";

        var diagnostic = Single(Contract(Lifecycle(states: states, transitions: transitions)));

        AssertDiagnostic(DiagnosticCodes.LifecycleStatesMismatch, "/lifecycle/states", diagnostic);
        Assert.Contains("'approved'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_is_compared_with_the_values_exactly()
    {
        var states = """[ { "name": "Draft", "initial": true }, { "name": "submitted" }, { "name": "approved" } ]""";
        var transitions = """[ { "name": "submit", "from": ["Draft"], "to": "submitted" } ]""";

        var diagnostics = Compile(Contract(Lifecycle(states: states, transitions: transitions)));

        Assert.Equal(
            [(DiagnosticCodes.LifecycleStatesMismatch, "/lifecycle/states"), (DiagnosticCodes.LifecycleStatesMismatch, "/lifecycle/states/0/name")],
            diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).Order());
    }

    [Fact]
    public void A_lifecycle_without_an_initial_state_is_axc0327_at_the_states()
    {
        var states = """[ { "name": "draft" }, { "name": "submitted" }, { "name": "approved" } ]""";

        var diagnostic = Single(Contract(Lifecycle(states: states)));

        AssertDiagnostic(DiagnosticCodes.NoInitialState, "/lifecycle/states", diagnostic);
    }

    [Fact]
    public void A_state_name_that_repeats_ignoring_letter_case_is_axc0328_at_the_later_state()
    {
        var states = """
            [ { "name": "draft", "initial": true }, { "name": "submitted" }, { "name": "approved" }, { "name": "draft" } ]
            """;

        var diagnostics = Compile(Contract(Lifecycle(states: states)));

        var diagnostic = Assert.Single(diagnostics);
        AssertDiagnostic(DiagnosticCodes.DuplicateContractName, "/lifecycle/states/3/name", diagnostic);
    }

    [Fact]
    public void A_transition_name_that_repeats_ignoring_letter_case_is_axc0328_at_the_later_transition()
    {
        var transitions = """
            [
              { "name": "submit", "from": ["draft"], "to": "submitted" },
              { "name": "Submit", "from": ["submitted"], "to": "approved" }
            ]
            """;

        var diagnostic = Single(Contract(Lifecycle(transitions: transitions)));

        AssertDiagnostic(DiagnosticCodes.DuplicateContractName, "/lifecycle/transitions/1/name", diagnostic);
    }

    [Theory]
    [InlineData("""{ "name": "submit", "from": ["draft", "nowhere"], "to": "submitted" }""", "/lifecycle/transitions/0/from/1")]
    [InlineData("""{ "name": "submit", "from": ["draft"], "to": "nowhere" }""", "/lifecycle/transitions/0/to")]
    public void A_transition_state_that_names_no_state_is_axc0329(string transition, string path)
    {
        var diagnostic = Single(Contract(Lifecycle(transitions: $"[{transition}]")));

        AssertDiagnostic(DiagnosticCodes.UnknownTransitionState, path, diagnostic);
        Assert.Contains("'nowhere'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pair_of_states_that_two_transitions_share_is_axc0330_at_the_later_from_and_names_the_first()
    {
        var transitions = """
            [
              { "name": "submit", "from": ["draft"], "to": "submitted" },
              { "name": "resubmit", "from": ["submitted", "draft"], "to": "submitted" }
            ]
            """;

        var diagnostics = Compile(Contract(Lifecycle(transitions: transitions)));

        var diagnostic = Assert.Single(diagnostics, candidate => candidate.Code == DiagnosticCodes.DuplicateTransitionPair);
        Assert.Equal("/lifecycle/transitions/1/from/1", diagnostic.Path);
        Assert.Contains("'submit'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_to_state_that_is_also_in_from_is_axc0331_at_the_to()
    {
        var transitions = """[ { "name": "submit", "from": ["draft", "submitted"], "to": "submitted" } ]""";

        var diagnostic = Single(Contract(Lifecycle(transitions: transitions)));

        AssertDiagnostic(DiagnosticCodes.TransitionToSameState, "/lifecycle/transitions/0/to", diagnostic);
    }

    [Fact]
    public void A_guard_that_is_not_boolean_reports_the_expression_code_at_the_guard()
    {
        var transitions = """
            [
              { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "IsSmall(amount)" },
              { "name": "approve", "from": ["submitted"], "to": "approved", "guard": "amount + 1" }
            ]
            """;

        var diagnostic = Single(Contract(Lifecycle(transitions: transitions)));

        AssertDiagnostic(ExpressionDiagnosticCodes.ResultTypeMismatch, "/lifecycle/transitions/1/guard", diagnostic);
    }

    [Fact]
    public void A_guard_that_names_an_unknown_field_reports_the_expression_code_at_the_guard()
    {
        var transitions = """[ { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "missing > 0" } ]""";

        var diagnostics = Compile(Contract(Lifecycle(transitions: transitions)));

        var diagnostic = Assert.Single(diagnostics, candidate => candidate.Path == "/lifecycle/transitions/0/guard");
        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, diagnostic.Code);
    }

    [Fact]
    public void A_guard_can_read_the_previous_value_of_a_field()
    {
        var transitions = """[ { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "previous(amount) == amount" } ]""";

        using var modules = Modules(Contract(Lifecycle(transitions: transitions)));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var guard = compiled.Model!.Entities.Single(entity => entity.Name == "approvals/Request").Contract!.Lifecycle!.Transitions[0].Guard;
        Assert.Equal("previous(amount) == amount", guard!.Expression);
    }

    [Fact]
    public void A_guard_that_reads_the_previous_value_of_a_child_collection_is_axc0337_at_the_guard()
    {
        var transitions = """[ { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "previous(lines) == null" } ]""";

        var diagnostics = Compile(Contract(Lifecycle(transitions: transitions)));

        var diagnostic = Assert.Single(diagnostics, candidate => candidate.Path == "/lifecycle/transitions/0/guard");
        Assert.Equal(ExpressionDiagnosticCodes.InvalidPreviousArgument, diagnostic.Code);
    }

    [Fact]
    public void A_message_text_key_without_the_module_prefix_is_axc0209_at_the_text_key()
    {
        var transitions = """
            [ { "name": "submit", "from": ["draft"], "to": "submitted", "message": { "textKey": "request.small" } } ]
            """;

        var diagnostic = Single(Contract(Lifecycle(transitions: transitions)));

        AssertDiagnostic(DiagnosticCodes.ModuleTextKeyWithoutPrefix, "/lifecycle/transitions/0/message/textKey", diagnostic);
        Assert.Contains("'approvals.'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_that_breaks_the_schema_is_a_schema_violation_and_is_not_checked_further()
    {
        var diagnostic = Single(Contract(Lifecycle(), extra: """, "extra": true"""));

        Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code);
        Assert.Equal(ContractFile, diagnostic.File);
    }

    [Fact]
    public void A_ceiling_slot_that_is_no_role_slot_of_the_module_is_axc0332_at_its_key_and_lists_the_slots()
    {
        var diagnostic = Single(Contract(null, extra: """, "ceiling": { "slots": { "x/y": [{ "actions": ["read"] }], "requester": [{ "actions": ["read"] }] } }"""));

        AssertDiagnostic(DiagnosticCodes.UnknownCeilingSlot, "/ceiling/slots/x~1y", diagnostic);
        Assert.Contains("'approver', 'requester'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ceiling_field_that_the_entity_lacks_is_axc0333_at_the_field_in_a_slot_and_in_others()
    {
        var diagnostics = Compile(Contract(null, extra: """
            , "ceiling": { "slots": { "requester": [{ "actions": ["read"] }, { "actions": ["read"], "fields": ["title", "Missing"] }] },
                           "others": [{ "actions": ["read"], "fields": ["gone"] }] }
            """));

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownCeilingField, "/ceiling/others/0/fields/0"),
                (DiagnosticCodes.UnknownCeilingField, "/ceiling/slots/requester/1/fields/1"),
            ],
            diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void A_ceiling_field_matches_a_field_of_the_entity_ignoring_letter_case()
    {
        using var modules = Modules(Contract(null, extra: """, "ceiling": { "others": [{ "actions": ["read"], "fields": ["TITLE"] }] }"""));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var others = compiled.Model!.Entities.Single(entity => entity.Name == "approvals/Request").Contract!.Ceiling!.Others!;
        Assert.Equal(["title"], Assert.Single(others).Fields);
    }

    [Theory]
    [InlineData("amount", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("missing > 1", ExpressionDiagnosticCodes.UnknownName)]
    public void A_ceiling_filter_that_is_not_a_valid_policy_filter_gives_its_expression_code_at_the_filter(string filter, string code)
    {
        var diagnostic = Single(Contract(null, extra: $$"""
            , "ceiling": { "slots": { "approver": [{ "actions": ["read"], "filter": "{{filter}}" }] } }
            """));

        AssertDiagnostic(code, "/ceiling/slots/approver/0/filter", diagnostic);
    }

    [Fact]
    public void A_ceiling_filter_that_is_a_valid_policy_filter_is_compiled_into_the_entry()
    {
        using var modules = Modules(Contract(null, extra: """
            , "ceiling": { "slots": { "requester": [{ "actions": ["read"], "filter": "amount > 100" }] } }
            """));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var ceiling = compiled.Model!.Entities.Single(entity => entity.Name == "approvals/Request").Contract!.Ceiling!;
        var entry = Assert.Single(ceiling.Roles["clerk"]);
        Assert.Equal("amount > 100", entry.Filter!.Expression);
        Assert.Equal([PolicyAction.Read], entry.Actions);
    }

    [Fact]
    public void A_ceiling_action_that_is_not_an_entity_action_is_a_schema_violation()
    {
        var diagnostic = Single(Contract(null, extra: """, "ceiling": { "others": [{ "actions": ["start"] }] }"""));

        Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code);
        Assert.Equal(ContractFile, diagnostic.File);
    }

    [Fact]
    public void A_core_validation_name_that_repeats_ignoring_letter_case_is_axc0328_at_the_name()
    {
        var diagnostic = Single(Contract(null, extra: Validations(
            Validation("amountSmall", "amount < 100"),
            Validation("AmountSmall", "amount < 1000"))));

        AssertDiagnostic(DiagnosticCodes.DuplicateContractName, "/validations/1/name", diagnostic);
        Assert.Contains("'AmountSmall'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_core_validation_field_the_entity_lacks_is_axc0052_at_the_field()
    {
        var diagnostic = Single(Contract(null, extra: Validations(Validation("amountSmall", "amount < 100", field: "Missing"))));

        AssertDiagnostic(DiagnosticCodes.UnknownValidationField, "/validations/0/field", diagnostic);
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_core_validation_message_key_without_the_module_prefix_is_axc0209_at_the_key()
    {
        var diagnostic = Single(Contract(null, extra: Validations(Validation("amountSmall", "amount < 100", textKey: "request.small"))));

        AssertDiagnostic(DiagnosticCodes.ModuleTextKeyWithoutPrefix, "/validations/0/message/textKey", diagnostic);
        Assert.Contains("approvals.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("amount", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("missing > 1", ExpressionDiagnosticCodes.UnknownName)]
    public void A_core_validation_expression_that_is_not_boolean_gives_its_expression_code_at_the_expression(string expression, string code)
    {
        var diagnostic = Single(Contract(null, extra: Validations(Validation("amountSmall", expression))));

        AssertDiagnostic(code, "/validations/0/expression", diagnostic);
    }

    [Fact]
    public void A_core_validation_may_call_previous_accountableUser_and_decisionTask_and_the_model_holds_it()
    {
        using var modules = Modules(Contract(null, extra: Validations(
            Validation("amountFrozen", "previous(status) == null or previous(status) == 'draft' or amount == previous(amount)"),
            Validation("owner", "accountableUser() != null or decisionTask() != null or amount == previous(amount)", field: "title"))));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var contract = model.Entities.Single(entity => entity.Name == "approvals/Request").Contract!;
            Assert.Equal(["amountFrozen", "owner"], contract.Validations.Select(validation => validation.Name));
            var frozen = contract.Validations[0];
            Assert.Equal("amount", frozen.Field);
            Assert.Equal("approvals.request.small", frozen.Message.TextKey);
            Assert.Equal("previous(status) == null or previous(status) == 'draft' or amount == previous(amount)", frozen.Expression.Expression);
            Assert.Equal("title", contract.Validations[1].Field);
            Assert.Null(contract.Lifecycle);
        }
    }

    private const string ScenarioFile = "modules/approvals/scenarios/Covers.json";

    private const string CoveredPointsExtra = """
        , "validations": [ { "name": "amountPositive", "expression": "amount >= 0", "message": { "textKey": "approvals.request.small" }, "field": "amount" } ],
          "effects": [ { "name": "submitted", "on": "submit", "create": { "entity": "Line", "values": { "quantity": "1" } } } ],
          "ceiling": { "slots": { "requester": [{ "actions": ["read"], "fields": ["title"] }] }, "others": [{ "actions": ["read"], "fields": ["title"] }] }
        """;

    [Fact]
    public void A_module_scenario_covering_one_point_of_each_form_compiles_and_keeps_its_covers()
    {
        string[] covers =
        [
            "Request/transitions/approve",
            "Request/states/approved/initial",
            "Request/states/approved/deletable",
            "Request/validations/amountPositive",
            "Request/effects/submitted",
            "Request/ceiling/requester",
            "Request/ceiling/others",
        ];
        using var modules = Modules(Contract(Lifecycle(), extra: CoveredPointsExtra), (ScenarioPath, CoversScenario(covers)));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var scenario = Assert.Single(compiled.ModuleScenarios);
        Assert.Equal(covers, scenario.Contract!.Covers);
    }

    [Fact]
    public void A_covered_point_the_contract_lacks_is_axc0339_at_its_index()
    {
        using var modules = Modules(
            Contract(Lifecycle()),
            (ScenarioPath, CoversScenario("Request/transitions/approve", "Request/transitions/publish", "Request/states/draft/initial")));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [(DiagnosticCodes.UnknownCoveredPoint, ScenarioFile, "/contract/covers/1"), (DiagnosticCodes.UnknownCoveredPoint, ScenarioFile, "/contract/covers/2")],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_covered_point_is_matched_with_the_letter_case_of_the_contract()
    {
        using var modules = Modules(Contract(Lifecycle()), (ScenarioPath, CoversScenario("request/transitions/approve")));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownCoveredPoint, "/contract/covers/0"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void An_empty_covers_list_is_a_schema_violation_of_the_scenario()
    {
        using var modules = Modules(Contract(Lifecycle()), (ScenarioPath, CoversScenario()));
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics, diagnostic => diagnostic.Path == "/contract/covers");
        Assert.Equal((DiagnosticCodes.SchemaViolation, ScenarioFile), (diagnostic.Code, diagnostic.File));
    }

    private static string CoversScenario(params string[] covers) => $$"""
        { "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b80", "kind": "scenario", "name": "Covers", "formatVersion": 1,
          "users": { "u1": ["requester"] },
          "contract": { "covers": [{{string.Join(", ", covers.Select(point => $"\"{point}\""))}}] },
          "steps": [ { "type": "createRecord", "entity": "Request", "save": "request", "values": { "title": "x", "status": "draft", "locked": "a" }, "as": "u1" } ] }
        """;

    private static void AssertDiagnostic(string code, string path, Diagnostic diagnostic)
    {
        Assert.Equal(
            (code, ContractFile, path, (Guid?)Guid.Parse(ContractId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
    }

    private static Diagnostic Single(string contract)
    {
        var diagnostics = Compile(contract);
        return Assert.Single(diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Compile(string contract)
    {
        using var modules = Modules(contract);
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Null(compiled.Model);
        return compiled.Diagnostics;
    }

    private static string Lifecycle(string states = DefaultStates, string transitions = DefaultTransitions, string field = "status") => $$"""
        { "field": "{{field}}", "states": {{states}}, "transitions": {{transitions}} }
        """;

    private static string Validations(params string[] validations) =>
        $$""", "validations": [ {{string.Join(", ", validations)}} ]""";

    private static string Validation(string name, string expression, string field = "amount", string textKey = "approvals.request.small") => $$"""
        { "name": "{{name}}", "expression": "{{expression}}", "message": { "textKey": "{{textKey}}" }, "field": "{{field}}" }
        """;

    private static string Seed(string status) => $$"""
        { "id": "{{SeedId}}", "kind": "seed", "name": "Requests", "formatVersion": 1, "entity": "Request",
          "records": [ { "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b74", "values": { "title": "A", "status": "{{status}}", "locked": "a" } } ] }
        """;

    private static string Contract(
        string? lifecycle,
        string entity = "Request",
        string name = "RequestContract",
        string id = ContractId,
        string extra = "")
    {
        var lifecycleProperty = lifecycle is null ? "" : $$""", "lifecycle": {{lifecycle}}""";
        return $$"""
            { "id": "{{id}}", "kind": "contract", "name": "{{name}}", "formatVersion": 1, "entity": "{{entity}}"{{lifecycleProperty}}{{extra}} }
            """;
    }

    private static TemporaryFolder Modules(string firstContract, params (string File, string Content)[] more)
    {
        var folder = Modules();
        folder.With("approvals/1.0.0/Contract.json", firstContract);
        foreach (var (file, content) in more)
        {
            folder.With($"approvals/1.0.0/{file}", content);
        }

        return folder;
    }

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("approvals/1.0.0/module.json", """
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals",
                  "roles": [ { "name": "requester", "label": { "textKey": "approvals.slot" } }, { "name": "approver", "label": { "textKey": "approvals.slot" } } ] }
                """)
            .With("approvals/1.0.0/Request.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21", "kind": "entity", "name": "Request", "formatVersion": 1,
                  "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "amount", "type": "integer" },
                    { "name": "status", "type": "enum", "values": ["draft", "submitted", "approved"], "required": true },
                    { "name": "stage", "type": "enum", "values": ["a", "b"] },
                    { "name": "locked", "type": "enum", "values": ["a", "b"], "required": true, "sensitive": true },
                    { "name": "lines", "type": "child-collection", "target": "Line" }
                  ] }
                """)
            .With("approvals/1.0.0/Line.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c22", "kind": "entity", "name": "Line", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ] }
                """)
            .With("approvals/1.0.0/IsSmall.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c23", "kind": "rule", "name": "IsSmall", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "value < 100" }
                """)
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.request.small": "Only small requests can be submitted.", "approvals.slot": "Slot" } }
                """);

    private static TemporaryFolder Application() =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "1.0.0", "roles": { "requester": "clerk", "approver": "lead" } }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "sample.title": "Sample", "role.clerk": "Clerk", "role.lead": "Lead" } }
                """)
            .With("roles/clerk.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }
                """)
            .With("roles/lead.json", """
                { "id": "44444444-4444-4444-8444-444444444412", "kind": "role", "name": "lead", "formatVersion": 1, "label": { "textKey": "role.lead" } }
                """);
}
