using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleRelaxTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e11";
    private const string ApplicationFile = "application.json";
    private const string Approve = "Request/transitions/approve";
    private const string Submit = "Request/transitions/submit";

    private const string States = """
        [ { "name": "draft", "initial": true, "deletable": true }, { "name": "submitted" }, { "name": "approved" } ]
        """;

    private const string SubmitTransition = """
        { "name": "submit", "from": ["draft"], "to": "submitted", "guard": "amount > 0" }
        """;

    private const string ApproveTransition = """
        { "name": "approve", "from": ["submitted"], "to": "approved", "guard": "amount > 0" }
        """;

    [Fact]
    public void An_unknown_point_is_reported_at_its_entry()
    {
        var relax = $$"""
            [ { "point": "{{Submit}}", "reason": "A" }, { "point": "Request/transitions/missing", "reason": "B" } ]
            """;

        var diagnostics = CompileFailing(relax);

        Assert.Equal(
            [(DiagnosticCodes.UnknownRelaxPoint, ApplicationFile, "/modules/0/relax/1")],
            diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_point_is_matched_with_its_letter_case()
    {
        var diagnostics = CompileFailing("""[ { "point": "request/transitions/submit", "reason": "A" } ]""");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownRelaxPoint, "/modules/0/relax/0"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_module_without_contracts_has_no_points()
    {
        using var modules = Modules("1.0.0", contract: null);
        using var folder = Application("^1.0.0", $$"""[ { "point": "{{Submit}}", "reason": "A" } ]""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownRelaxPoint, "/modules/0/relax/0"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("has no contracts", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_point_named_twice_is_reported_at_the_second_entry_only()
    {
        var relax = $$"""
            [ { "point": "{{Submit}}", "reason": "A" }, { "point": "{{Approve}}", "reason": "B" }, { "point": "{{Submit}}", "reason": "C" } ]
            """;

        var diagnostics = CompileFailing(relax);

        Assert.Equal(
            [(DiagnosticCodes.DuplicateRelaxPoint, ApplicationFile, "/modules/0/relax/2")],
            diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Theory]
    [InlineData("""{ "point": "Request/transitions/submit" }""")]
    [InlineData("""{ "point": "Request/transitions/submit", "reason": "" }""")]
    [InlineData("""{ "point": "Request/transitions/submit", "reason": "  \t " }""")]
    public void A_missing_or_blank_reason_is_reported_at_its_entry(string entry)
    {
        var relax = $$"""[ { "point": "{{Approve}}", "reason": "A" }, {{entry}} ]""";

        var diagnostics = CompileFailing(relax);

        Assert.Equal(
            [(DiagnosticCodes.MissingRelaxReason, ApplicationFile, "/modules/0/relax/1")],
            diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_reason_of_more_than_500_characters_is_a_schema_violation()
    {
        var relax = $$"""[ { "point": "{{Submit}}", "reason": "{{new string('a', 501)}}" } ]""";

        var diagnostics = CompileFailing(relax);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, ApplicationFile, "/modules/0/relax/0/reason"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_relax_entry_without_a_point_is_a_schema_violation()
    {
        var diagnostics = CompileFailing("""[ { "reason": "A" } ]""");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, ApplicationFile), (diagnostic.Code, diagnostic.File));
    }

    [Fact]
    public void A_point_that_a_newer_pinned_version_lacks_is_unknown()
    {
        using var modules = Modules("1.0.0", Contract(SubmitTransition, ApproveTransition))
            .With("approvals/1.1.0/module.json", ModuleManifest("1.1.0"))
            .With("approvals/1.1.0/Request.json", Request)
            .With("approvals/1.1.0/TextsEn.json", Texts)
            .With("approvals/1.1.0/Contract.json", Contract(SubmitTransition));
        using var folder = Application("^1.0.0", $$"""[ { "point": "{{Approve}}", "reason": "A" } ]""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Null(compiled.Model);
        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownRelaxPoint, ApplicationFile, "/modules/0/relax/0"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("1.1.0", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_entries_are_kept_on_the_pin_in_the_order_written()
    {
        var relax = $$"""
            [ { "point": "{{Submit}}", "reason": "  We are three people.  " }, { "point": "{{Approve}}", "reason": "The director approves." } ]
            """;
        using var modules = Modules("1.0.0", Contract(SubmitTransition, ApproveTransition));
        using var folder = Application("^1.0.0", relax);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var pin = Assert.Single(compiled.Model!.Modules);
        Assert.Equal(
            [new RelaxedPointModel(Submit, "  We are three people.  "), new RelaxedPointModel(Approve, "The director approves.")],
            pin.Relaxed);
    }

    [Fact]
    public void A_module_entry_without_relax_has_no_relaxed_points()
    {
        using var modules = Modules("1.0.0", Contract(SubmitTransition, ApproveTransition));
        using var folder = Application("^1.0.0", relax: null);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(Assert.Single(compiled.Model!.Modules).Relaxed);
    }

    [Fact]
    public void A_relaxed_initial_rule_lets_a_seed_record_in_that_state_compile()
    {
        using var modules = Modules("1.0.0", Contract(SubmitTransition, ApproveTransition), SubmittedSeed);
        using var refused = Application("^1.0.0", relax: null);
        using var relaxed = Application("^1.0.0", """[ { "point": "Request/states/submitted/initial", "reason": "Imported requests." } ]""");

        var catalog = ModuleCatalog.Load(modules.Path).Modules;
        var before = ApplicationCompiler.Compile(refused.Path, null, catalog);
        var after = ApplicationCompiler.Compile(relaxed.Path, null, catalog);

        Assert.Equal([DiagnosticCodes.SeedStateNotInitial], before.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Empty(after.Diagnostics);
        Assert.Single(after.Model!.Seeds);
    }

    [Fact]
    public void A_relaxed_transition_compiles_without_its_guard_and_the_others_keep_theirs()
    {
        using var modules = Modules("1.0.0", Contract(SubmitTransition, ApproveTransition));
        using var folder = Application("^1.0.0", $$"""[ { "point": "{{Submit}}", "reason": "No amount rule." } ]""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var transitions = compiled.Model!.Entities.Single(entity => entity.Name == "approvals/Request").Contract!.Lifecycle!.Transitions;
        Assert.Null(transitions.Single(transition => transition.Name == "submit").Guard);
        Assert.NotNull(transitions.Single(transition => transition.Name == "approve").Guard);
    }

    private static IReadOnlyList<Diagnostic> CompileFailing(string relax)
    {
        using var modules = Modules("1.0.0", Contract(SubmitTransition, ApproveTransition));
        using var folder = Application("^1.0.0", relax);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Null(compiled.Model);
        return compiled.Diagnostics;
    }

    private static string Contract(params string[] transitions) => $$"""
        { "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b71", "kind": "contract", "name": "RequestContract", "formatVersion": 1, "entity": "Request",
          "lifecycle": { "field": "status", "states": {{States}}, "transitions": [ {{string.Join(", ", transitions)}} ] } }
        """;

    private static string ModuleManifest(string version) => $$"""
        { "kind": "module", "id": "approvals", "version": "{{version}}", "label": "Approvals",
          "roles": [ { "name": "requester", "label": { "textKey": "approvals.slot" } } ] }
        """;

    private const string Request = """
        { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21", "kind": "entity", "name": "Request", "formatVersion": 1, "displayField": "title",
          "fields": [
            { "name": "title", "type": "text", "required": true },
            { "name": "amount", "type": "integer" },
            { "name": "status", "type": "enum", "values": ["draft", "submitted", "approved"], "required": true }
          ] }
        """;

    private const string Texts = """
        { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "approvals.slot": "Slot" } }
        """;

    private const string SubmittedSeed = """
        { "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b73", "kind": "seed", "name": "Requests", "formatVersion": 1, "entity": "Request",
          "records": [ { "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b74", "values": { "title": "A", "status": "submitted" } } ] }
        """;

    private static TemporaryFolder Modules(string version, string? contract, string? seed = null)
    {
        var folder = new TemporaryFolder()
            .With($"approvals/{version}/module.json", ModuleManifest(version))
            .With($"approvals/{version}/Request.json", Request)
            .With($"approvals/{version}/TextsEn.json", Texts);
        if (seed is not null)
        {
            folder.With($"approvals/{version}/Seed.json", seed);
        }

        return contract is null ? folder : folder.With($"approvals/{version}/Contract.json", contract);
    }

    private static TemporaryFolder Application(string range, string? relax)
    {
        var relaxProperty = relax is null ? "" : $$""", "relax": {{relax}}""";
        return new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "{{range}}", "roles": { "requester": "clerk" }{{relaxProperty}} }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "sample.title": "Sample", "role.clerk": "Clerk" } }
                """)
            .With("roles/clerk.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }
                """);
    }
}
