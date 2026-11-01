using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleScenarioTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ScenarioId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c61";
    private const string ScenarioFile = "modules/approvals/scenarios/Create.json";

    [Fact]
    public void A_module_scenario_comes_back_named_by_module_with_the_roles_its_slots_map_to()
    {
        using var modules = Modules(users: """{ "u1": ["approver"], "u2": [] }""", assignee: """{ "role": "approver" }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var scenario = Assert.Single(compiled.ModuleScenarios);
        Assert.Equal("approvals/Create", scenario.Name);
        Assert.Equal(ScenarioFile, scenario.File);
        Assert.Equal(Guid.Parse(ScenarioId), scenario.Id);
        Assert.Equal(["manager"], scenario.Users["u1"]);
        Assert.Empty(scenario.Users["u2"]);
        Assert.Equal("approvals/Approval", scenario.Steps[0].Entity);
        Assert.Equal("manager", scenario.Steps[1].Assignee!.Role);
    }

    [Fact]
    public void A_module_scenario_stays_out_of_the_release_and_its_content_hash()
    {
        using var modules = Modules(users: """{ "u1": ["approver"] }""");
        using var folder = Application();
        using var without = Modules(users: """{ "u1": ["approver"] }""", scenario: false);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var baseline = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(without.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.DoesNotContain(compiled.Resources, resource => resource.Path.Contains("/scenarios/", StringComparison.Ordinal));
        Assert.Contains(compiled.Resources, resource => resource.Path == "modules/approvals/Approval.json");
        Assert.Equal(baseline.ContentHash, compiled.ContentHash);
        Assert.Empty(baseline.ModuleScenarios);
    }

    [Fact]
    public void A_module_scenario_user_role_that_is_an_application_role_is_axc0320_in_the_module_file()
    {
        using var modules = Modules(users: """{ "u1": ["manager"] }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleScenarioRoleNotSlot, ScenarioFile, "/users/u1/0", Guid.Parse(ScenarioId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'manager'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'approver'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
        Assert.Empty(compiled.ModuleScenarios);
    }

    [Fact]
    public void A_module_scenario_assignee_and_recipient_role_that_is_an_application_role_is_axc0320()
    {
        using var modules = Modules(
            users: """{ "u1": ["approver"] }""",
            assignee: """{ "role": "manager" }""",
            recipient: """{ "role": "manager" }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (DiagnosticCodes.ModuleScenarioRoleNotSlot, ScenarioFile, "/steps/1/assignee/role"),
                (DiagnosticCodes.ModuleScenarioRoleNotSlot, ScenarioFile, "/steps/2/recipient/role"),
            ],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_module_scenario_in_a_module_without_slots_says_the_module_has_none()
    {
        using var modules = Modules(users: """{ "u1": ["approver"] }""", slots: false);
        using var folder = Application(mapping: null);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleScenarioRoleNotSlot, "/users/u1/0"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("has no role slots", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_scenario_step_that_names_a_resource_of_another_module_is_axc0208_in_the_module_file()
    {
        using var modules = Modules(users: """{ "u1": ["approver"] }""", entity: "departments/Department");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, ScenarioFile, "/steps/0/entity", Guid.Parse(ScenarioId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.DoesNotContain(compiled.Resources, resource => resource.Path.Contains("/scenarios/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_module_scenario_step_that_names_an_application_entity_is_an_unknown_entity_in_the_module_file()
    {
        using var modules = Modules(users: """{ "u1": ["approver"] }""", entity: "Invoice");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownScenarioEntity, ScenarioFile, "/steps/0/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private static TemporaryFolder Modules(
        string users,
        string? assignee = null,
        string? recipient = null,
        string entity = "Approval",
        bool slots = true,
        bool scenario = true)
    {
        var slotList = slots ? """, "roles": [ { "name": "approver", "label": { "textKey": "approvals.approver" } } ]""" : "";
        var assigneeStep = assignee is null ? "" : $$""", { "type": "expectTask", "record": "approval", "step": "review", "assignee": {{assignee}} }""";
        var recipientStep = recipient is null ? "" : $$""", { "type": "expectNotification", "recipient": {{recipient}} }""";
        var folder = new TemporaryFolder()
            .With("approvals/1.0.0/module.json", $$"""
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals"{{slotList}} }
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
                      "outcomes": [ { "name": "approve", "label": { "textKey": "approvals.approve" }, "next": "done" } ] },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.approval": "Approval", "approvals.review": "Review", "approvals.approve": "Approve", "approvals.approver": "Approver" } }
                """);
        return scenario
            ? folder.With("approvals/1.0.0/scenarios/Create.json", $$"""
                { "id": "{{ScenarioId}}", "kind": "scenario", "name": "Create", "formatVersion": 1,
                  "users": {{users}},
                  "steps": [ { "type": "createRecord", "entity": "{{entity}}", "save": "approval", "values": { "title": "x" }, "as": "u1" }{{assigneeStep}}{{recipientStep}} ] }
                """)
            : folder;
    }

    private static TemporaryFolder Application(string? mapping = """{ "approver": "manager" }""")
    {
        var roles = mapping is null ? "" : $$""", "roles": {{mapping}}""";
        return new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "1.0.0"{{roles}} }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "invoice.title": "Invoice", "role.manager": "Manager" } }
                """)
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "manager", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """)
            .With("entities/Invoice.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "entity", "name": "Invoice", "formatVersion": 1,
                  "fields": [ { "name": "number", "type": "text" } ] }
                """);
    }
}
