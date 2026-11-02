using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ContractLayerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e21";
    private const string ContractLayerId = "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c11";
    private const string EntityLayerId = "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c12";
    private const string ContractLayerFile = "customizations/approvals/RequestContract.json";
    private const string EntityLayerFile = "customizations/approvals/Request.json";

    private const string OnHold = """{ "name": "onHold" }""";
    private const string Hold = """{ "name": "hold", "from": ["submitted"], "to": "onHold", "guard": "amount < 1000" }""";
    private const string Resume = """{ "name": "resume", "from": ["onHold"], "to": "submitted", "guard": "amount > 0" }""";
    private const string AllValues = """{ "name": "status", "values": ["draft", "submitted", "approved", "onHold"] }""";

    [Fact]
    public void A_layer_that_adds_a_state_its_value_and_transitions_compiles()
    {
        var compiled = Compile(Contract(states: OnHold, transitions: $"{Hold}, {Resume}"), Entity(AllValues));

        Assert.Empty(compiled.Diagnostics);
        var lifecycle = compiled.Model!.Entities.Single(entity => entity.Name == "approvals/Request").Contract!.Lifecycle!;
        Assert.Contains(lifecycle.States, state => state.Name == "onHold" && !state.Protected);
        Assert.Contains(lifecycle.States, state => state.Name == "approved" && state.Protected);
        Assert.All(lifecycle.Transitions.Where(transition => transition.Name is "hold" or "resume"), transition => Assert.True(transition.Added));
        Assert.All(lifecycle.Transitions.Where(transition => transition.Name is "submit" or "approve"), transition => Assert.False(transition.Added));
        Assert.Contains(compiled.Resources, resource => resource.Path == ContractLayerFile);
    }

    [Fact]
    public void An_added_transition_may_use_a_text_key_without_the_module_prefix()
    {
        var hold = """{ "name": "hold", "from": ["submitted"], "to": "onHold", "guard": "amount < 1000", "message": { "textKey": "sample.holdRefused" } }""";

        var compiled = Compile(Contract(states: OnHold, transitions: hold), Entity(AllValues));

        Assert.Empty(compiled.Diagnostics);
    }

    [Fact]
    public void An_added_transition_into_a_protected_state_is_reported_at_its_to()
    {
        var skip = """{ "name": "skip", "from": ["onHold"], "to": "approved" }""";

        var compiled = Compile(Contract(states: OnHold, transitions: $"{Hold}, {skip}"), Entity(AllValues));

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.AddedTransitionIntoProtectedState, ContractLayerFile, "/changes/lifecycle/transitions/1/to", Guid.Parse(ContractLayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    [Theory]
    [InlineData("""{ "lifecycle": { "transitions": [ { "name": "approve", "from": ["submitted"], "to": "approved", "guard": "amount > 100" } ] } }""", "/changes/lifecycle/transitions/0")]
    [InlineData("""{ "lifecycle": { "transitions": [ { "name": "Approve", "from": ["submitted"], "to": "approved" } ] } }""", "/changes/lifecycle/transitions/0")]
    [InlineData("""{ "lifecycle": { "states": [ { "name": "submitted", "deletable": true } ] } }""", "/changes/lifecycle/states/0")]
    [InlineData("""{ "lifecycle": { "transitions": [ { "name": "submit", "remove": true } ] } }""", "/changes/lifecycle/transitions/0")]
    [InlineData("""{ "lifecycle": { "states": [ { "name": "onHold", "protected": true } ] } }""", "/changes/lifecycle/states/0/protected")]
    [InlineData("""{ "lifecycle": { "field": "status" } }""", "/changes/lifecycle/field")]
    [InlineData("""{ "validations": [ { "name": "positive", "expression": "amount > 0", "message": { "textKey": "approvals.positive" }, "field": "amount" } ] }""", "/changes/validations")]
    public void A_layer_that_does_more_than_add_states_and_transitions_is_reported_and_not_applied(string changes, string path)
    {
        var compiled = Compile(changes, null);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ContractLayerNotAddition, ContractLayerFile, path, Guid.Parse(ContractLayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_state_added_only_in_the_contract_is_reported_at_its_name()
    {
        var compiled = Compile(Contract(states: OnHold, transitions: Hold), null);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.LifecycleAdditionMismatch, ContractLayerFile, "/changes/lifecycle/states/0/name", Guid.Parse(ContractLayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'onHold'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_added_only_to_the_entity_is_reported_at_its_values()
    {
        var compiled = Compile(null, Entity(AllValues));

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.LifecycleAdditionMismatch, EntityLayerFile, "/changes/fields/0/values", Guid.Parse(EntityLayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'onHold'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relax_entry_naming_an_added_transition_names_a_point_the_module_lacks()
    {
        var relax = """[ { "point": "Request/transitions/hold", "reason": "Held requests." }, { "point": "Request/states/onHold/deletable", "reason": "Held requests." } ]""";

        var compiled = Compile(Contract(states: OnHold, transitions: $"{Hold}, {Resume}"), Entity(AllValues), relax);

        Assert.Equal(
            [(DiagnosticCodes.UnknownRelaxPoint, "application.json", "/modules/0/relax/0"), (DiagnosticCodes.UnknownRelaxPoint, "application.json", "/modules/0/relax/1")],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_relax_entry_naming_a_transition_of_the_module_still_works_with_a_layer()
    {
        var relax = """[ { "point": "Request/transitions/submit", "reason": "No amount rule." } ]""";

        var compiled = Compile(Contract(states: OnHold, transitions: $"{Hold}, {Resume}"), Entity(AllValues), relax);

        Assert.Empty(compiled.Diagnostics);
    }

    private static CompilationResult Compile(string? contractChanges, string? entityChanges, string? relax = null)
    {
        using var modules = Modules();
        using var folder = Application(relax);
        if (contractChanges is not null)
        {
            folder.With(ContractLayerFile, Layer("approvals/RequestContract", ContractLayerId, contractChanges));
        }

        if (entityChanges is not null)
        {
            folder.With(EntityLayerFile, Layer("approvals/Request", EntityLayerId, entityChanges));
        }

        return ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
    }

    private static string Contract(string states, string transitions) =>
        $$"""{ "lifecycle": { "states": [ {{states}} ], "transitions": [ {{transitions}} ] } }""";

    private static string Entity(string statusField) => $$"""{ "fields": [ {{statusField}} ] }""";

    private static string Layer(string target, string id, string changes) => $$"""
        { "id": "{{id}}", "kind": "layer", "formatVersion": 1, "target": "{{target}}", "changes": {{changes}} }
        """;

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("approvals/1.0.0/module.json", """
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals",
                  "roles": [ { "name": "requester", "label": { "textKey": "approvals.slot" } } ] }
                """)
            .With("approvals/1.0.0/Request.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21", "kind": "entity", "name": "Request", "formatVersion": 1, "displayField": "title",
                  "fields": [
                    { "name": "title", "type": "text", "required": true },
                    { "name": "amount", "type": "integer" },
                    { "name": "status", "type": "enum", "values": ["draft", "submitted", "approved"], "required": true }
                  ] }
                """)
            .With("approvals/1.0.0/RequestContract.json", """
                { "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b71", "kind": "contract", "name": "RequestContract", "formatVersion": 1, "entity": "Request",
                  "lifecycle": { "field": "status",
                    "states": [ { "name": "draft", "initial": true, "deletable": true }, { "name": "submitted" }, { "name": "approved", "protected": true } ],
                    "transitions": [
                      { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "amount > 0" },
                      { "name": "approve", "from": ["submitted"], "to": "approved", "guard": "amount > 0" } ] } }
                """)
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.slot": "Slot", "approvals.positive": "Positive" } }
                """);

    private static TemporaryFolder Application(string? relax)
    {
        var relaxProperty = relax is null ? "" : $$""", "relax": {{relax}}""";
        return new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "1.0.0", "roles": { "requester": "clerk" }{{relaxProperty}} }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "sample.title": "Sample", "sample.holdRefused": "Held", "role.clerk": "Clerk" } }
                """)
            .With("roles/clerk.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }
                """);
    }
}
