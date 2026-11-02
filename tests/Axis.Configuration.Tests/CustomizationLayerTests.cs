using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class CustomizationLayerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string LayerId = "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c01";
    private const string SecondLayerId = "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c02";
    private const string ReviewFile = "customizations/approvals/Review.json";
    private const string ReviewChanges = """{ "steps": [ { "name": "review", "dueIn": "P3D" } ] }""";

    [Fact]
    public void A_layer_on_a_process_of_a_declared_module_compiles_and_is_part_of_the_release()
    {
        using var modules = Modules();
        using var folder = Application().With(ReviewFile, Layer("approvals/Review"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Contains(compiled.Resources, resource => resource.Path == ReviewFile);
        Assert.NotNull(compiled.Model);
    }

    [Theory]
    [InlineData("approvals/Nope")]
    [InlineData("people/Review")]
    [InlineData("departments/Department")]
    public void A_target_that_names_nothing_of_a_declared_module_is_reported_at_the_target(string target)
    {
        using var modules = Modules();
        using var folder = Application().With($"customizations/{target}.json", Layer(target));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownLayerTarget, $"customizations/{target}.json", "/target", Guid.Parse(LayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains($"'{target}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_that_the_application_only_reaches_through_another_module_is_said_not_to_be_declared()
    {
        using var modules = Modules();
        using var folder = Application().With("customizations/departments/Department.json", Layer("departments/Department"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Contains("not declared", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approvals/ReviewScenario")]
    [InlineData("approvals/module")]
    public void A_scenario_or_module_manifest_target_is_not_allowed(string target)
    {
        using var modules = Modules();
        using var folder = Application().With($"customizations/{target}.json", Layer(target));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.LayerTargetNotAllowed, $"customizations/{target}.json", "/target", Guid.Parse(LayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_name_that_more_than_one_kind_of_resource_has_is_ambiguous()
    {
        using var modules = Modules();
        using var folder = Application().With("customizations/approvals/Shared.json", Layer("approvals/Shared"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.AmbiguousLayerTarget, "customizations/approvals/Shared.json", "/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'dataSource', 'page'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_resource_name_is_matched_ignoring_letter_case_but_the_file_path_is_not()
    {
        using var modules = Modules();
        using var folder = Application().With("customizations/approvals/review.json", Layer("approvals/review"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);

        using var misplaced = Application().With("customizations/approvals/Review.json", Layer("approvals/review"));
        var misplacedResult = ApplicationCompiler.Compile(misplaced.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(misplacedResult.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MisplacedLayer, "customizations/approvals/Review.json", "/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("customizations/approvals/review.json", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layer_outside_its_path_is_reported_at_the_target()
    {
        using var modules = Modules();
        using var folder = Application().With("layers/review.json", Layer("approvals/Review"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MisplacedLayer, "layers/review.json", "/target", Guid.Parse(LayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains(ReviewFile, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approvals/Review")]
    [InlineData("approvals/review")]
    public void A_second_layer_on_one_target_is_reported_on_the_later_file(string secondTarget)
    {
        using var modules = Modules();
        using var folder = Application()
            .With(ReviewFile, Layer("approvals/Review"))
            .With("customizations/approvals/z-review.json", Layer(secondTarget, SecondLayerId));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var duplicate = Assert.Single(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.DuplicateLayerTarget);
        Assert.Equal(("customizations/approvals/z-review.json", "/target", Guid.Parse(SecondLayerId)), (duplicate.File, duplicate.Path, duplicate.ResourceId));
        Assert.Contains(ReviewFile, duplicate.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.File == ReviewFile);
    }

    [Theory]
    [InlineData("id", "\"7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c99\"")]
    [InlineData("kind", "\"form\"")]
    [InlineData("name", "\"Other\"")]
    [InlineData("formatVersion", "2")]
    public void A_layer_that_changes_the_identity_of_its_target_is_reported_at_the_property(string property, string value)
    {
        using var modules = Modules();
        using var folder = Application().With(ReviewFile, Layer("approvals/Review", changes: $$"""{ "{{property}}": {{value}} }"""));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.LayerChangesIdentity, ReviewFile, $"/changes/{property}", Guid.Parse(LayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains($"'{property}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layer_changes_the_content_hash_and_a_rebuild_from_the_release_compiles_the_same()
    {
        using var modules = Modules();
        var installed = ModuleCatalog.Load(modules.Path).Modules;
        using var plain = Application();
        using var layered = Application().With(ReviewFile, Layer("approvals/Review"));
        using var changed = Application().With(ReviewFile, Layer("approvals/Review", changes: """{ "steps": [ { "name": "review", "dueIn": "P5D" } ] }"""));

        var withoutLayer = ApplicationCompiler.Compile(plain.Path, null, installed);
        var first = ApplicationCompiler.Compile(layered.Path, null, installed);
        var second = ApplicationCompiler.Compile(changed.Path, null, installed);
        var rebuilt = ApplicationCompiler.Compile(first.Resources);

        Assert.Empty(first.Diagnostics);
        Assert.Empty(second.Diagnostics);
        Assert.NotEqual(withoutLayer.ContentHash, first.ContentHash);
        Assert.NotEqual(first.ContentHash, second.ContentHash);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal(first.ContentHash, rebuilt.ContentHash);
    }

    [Fact]
    public void A_layer_changes_the_process_of_the_module_and_leaves_the_rest_as_it_was()
    {
        using var modules = Modules();
        var installed = ModuleCatalog.Load(modules.Path).Modules;
        using var plain = Application();
        using var layered = Application().With(ReviewFile, Layer("approvals/Review", changes: """
            { "steps": [
                { "name": "review", "dueIn": "P3D",
                  "outcomes": [
                    { "name": "reject", "remove": true },
                    { "name": "escalate", "label": { "textKey": "approvals.escalate" }, "next": "archive" } ] },
                { "name": "archive", "type": "end" } ] }
            """));

        var before = Assert.Single(ApplicationCompiler.Compile(plain.Path, null, installed).Model!.Processes);
        var compiled = ApplicationCompiler.Compile(layered.Path, null, installed);

        Assert.Empty(compiled.Diagnostics);
        var after = Assert.Single(compiled.Model!.Processes);
        Assert.Equal(["review", "done", "archive"], after.Steps.Select(step => step.Name));
        var review = Assert.IsType<TaskStepModel>(after.Steps[0]);
        var original = Assert.IsType<TaskStepModel>(before.Steps[0]);
        Assert.Equal(TimeSpan.FromDays(3), review.DueIn);
        Assert.Equal(["approve", "escalate"], review.Outcomes.Select(outcome => outcome.Name));
        Assert.Equal("archive", review.Outcomes[1].Next);
        Assert.Equal(["approve", "reject"], original.Outcomes.Select(outcome => outcome.Name));
        Assert.Equal((original.Label, original.Form, original.Assignee.User!.Expression, original.Deadline), (review.Label, review.Form, review.Assignee.User!.Expression, review.Deadline));
        Assert.Equal(original.Outcomes[0], review.Outcomes[0]);
        Assert.Equal(before.Steps[1], after.Steps[1]);
        Assert.Equal((before.Name, before.Start, before.Entity), (after.Name, after.Start, after.Entity));
        Assert.Equal(
            Assert.Single(compiled.Resources, resource => resource.Path == "modules/approvals/processes/Review.json").Content,
            Assert.Single(ApplicationCompiler.Compile(plain.Path, null, installed).Resources, resource => resource.Path == "modules/approvals/processes/Review.json").Content);
    }

    [Theory]
    [InlineData("""{ "steps": [ { "name": "missing", "remove": true } ] }""", DiagnosticCodes.UnknownLayerKey, "/changes/steps/0/name")]
    [InlineData("""{ "steps": [ { "name": "review", "outcomes": [ { "name": "Nope", "remove": true } ] } ] }""", DiagnosticCodes.UnknownLayerKey, "/changes/steps/0/outcomes/0/name")]
    [InlineData("""{ "steps": [ { "name": "done", "type": "end" }, { "name": "review", "outcomes": [ { "name": "reject", "remove": true, "next": "done" } ] } ] }""", DiagnosticCodes.InvalidLayerRemove, "/changes/steps/1/outcomes/0")]
    [InlineData("""{ "steps": [ { "name": "review", "colour": "red" } ] }""", DiagnosticCodes.UnknownLayerProperty, "/changes/steps/0/colour")]
    [InlineData("""{ "steps": [ { "name": "extra", "type": "end", "colour": "red" } ] }""", DiagnosticCodes.UnknownLayerProperty, "/changes/steps/0/colour")]
    [InlineData("""{ "colour": "red" }""", DiagnosticCodes.UnknownLayerProperty, "/changes/colour")]
    public void A_change_that_cannot_apply_is_reported_at_its_path_in_the_layer_and_not_applied(string changes, string code, string path)
    {
        using var modules = Modules();
        using var folder = Application().With(ReviewFile, Layer("approvals/Review", changes: changes));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (code, ReviewFile, path, Guid.Parse(LayerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    private static string Layer(string target, string id = LayerId, string changes = ReviewChanges) => $$"""
        { "id": "{{id}}", "kind": "layer", "formatVersion": 1, "target": "{{target}}", "changes": {{changes}} }
        """;

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c31", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("departments/1.0.0/TextsEn.json", """
                { "id": "d9f3a5b7-4c6e-4a81-8d1c-2e3f4a5b6c7d", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "departments.department": "Department" } }
                """)
            .With("approvals/1.0.0/module.json", """
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals",
                  "modules": [ { "id": "departments", "version": "^1.0.0" } ] }
                """)
            .With("approvals/1.0.0/Approval.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21", "kind": "entity", "name": "Approval", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" } ] }
                """)
            .With("approvals/1.0.0/forms/ApprovalForm.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c24", "kind": "form", "name": "ApprovalForm", "formatVersion": 1, "entity": "Approval",
                  "sections": [ { "title": { "textKey": "approvals.approval" }, "fields": [ { "field": "title" } ] } ] }
                """)
            .With("approvals/1.0.0/processes/Review.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41", "kind": "process", "name": "Review", "formatVersion": 1,
                  "entity": "Approval", "start": "review", "steps": [
                    { "name": "review", "type": "task", "label": { "textKey": "approvals.review" }, "assignee": { "user": "'u1'" }, "form": "ApprovalForm",
                      "outcomes": [
                        { "name": "approve", "label": { "textKey": "approvals.approve" }, "next": "done" },
                        { "name": "reject", "label": { "textKey": "approvals.reject" }, "next": "done" } ] },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With("approvals/1.0.0/data-sources/Shared.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c61", "kind": "dataSource", "name": "Shared", "formatVersion": 1,
                  "entity": "Approval", "fields": [ { "name": "title", "path": "title" } ] }
                """)
            .With("approvals/1.0.0/pages/Shared.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c51", "kind": "page", "name": "Shared", "formatVersion": 1,
                  "title": { "textKey": "approvals.reviews" },
                  "widgets": [ { "type": "table", "dataSource": "Shared" } ] }
                """)
            .With("approvals/1.0.0/scenarios/ReviewScenario.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c71", "kind": "scenario", "name": "ReviewScenario", "formatVersion": 1,
                  "users": { "u1": [] },
                  "steps": [ { "type": "createRecord", "entity": "Approval", "save": "approval", "values": { "title": "x" }, "as": "u1" } ] }
                """)
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.approval": "Approval", "approvals.review": "Review", "approvals.approve": "Approve", "approvals.reject": "Reject", "approvals.escalate": "Escalate", "approvals.reviews": "Reviews" } }
                """);

    private static TemporaryFolder Application() =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "1.0.0" }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "invoice.title": "Invoice" } }
                """);
}
