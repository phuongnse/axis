using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleTaskAssigneeTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ProcessId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41";
    private const string ProcessFile = "modules/approvals/processes/Review.json";

    [Fact]
    public void A_module_task_and_escalation_reassign_that_name_a_slot_go_to_the_role_it_maps_to()
    {
        using var modules = Modules(assignee: """{ "role": "approver" }""", reassign: """{ "role": "approver" }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var step = Assert.IsType<TaskStepModel>(Assert.Single(model.Processes).Steps[0]);
            Assert.Equal("manager", step.Assignee.Role);
            Assert.Equal("manager", Assert.Single(step.Escalations).Reassign!.Role);
        }
    }

    [Fact]
    public void A_module_task_that_names_a_role_instead_of_a_slot_is_axc0307_and_not_axc0089()
    {
        using var modules = Modules(assignee: """{ "role": "manager" }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleTaskRoleNotSlot, ProcessFile, "/steps/0/assignee/role", Guid.Parse(ProcessId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'manager'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'approver'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_task_slot_name_is_compared_exactly()
    {
        using var modules = Modules(assignee: """{ "role": "Approver" }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleTaskRoleNotSlot, "/steps/0/assignee/role"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void An_escalation_reassign_that_names_a_role_instead_of_a_slot_is_axc0307()
    {
        using var modules = Modules(assignee: """{ "role": "approver" }""", reassign: """{ "role": "manager" }""");
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleTaskRoleNotSlot, ProcessFile, "/steps/0/escalations/0/reassign/role"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_task_in_a_module_without_slots_says_the_module_has_none()
    {
        using var modules = Modules(assignee: """{ "role": "approver" }""", slots: false);
        using var folder = Application(mapping: null);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleTaskRoleNotSlot, "/steps/0/assignee/role"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("has no role slots", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_application_task_that_names_an_unknown_role_is_still_axc0089()
    {
        using var modules = Modules(assignee: """{ "user": "'u1'" }""");
        using var folder = Application().With("processes/Audit.json", """
            { "id": "44444444-4444-4444-8444-444444444431", "kind": "process", "name": "Audit", "formatVersion": 1,
              "entity": "Invoice", "start": "check", "steps": [
                { "name": "check", "type": "task", "label": { "textKey": "audit.check" }, "assignee": { "role": "approver" }, "form": "InvoiceForm",
                  "outcomes": [ { "name": "close", "label": { "textKey": "audit.check" }, "next": "done" } ] },
                { "name": "done", "type": "end" }
              ] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownTaskRole, "processes/Audit.json", "/steps/0/assignee/role"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private static TemporaryFolder Modules(string assignee, string? reassign = null, bool slots = true)
    {
        var slotList = slots ? """, "roles": [ { "name": "approver", "label": { "textKey": "approvals.approver" } } ]""" : "";
        var escalations = reassign is null ? "" : $$""", "escalations": [ { "after": "PT1H", "reassign": {{reassign}} } ]""";
        return new TemporaryFolder()
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
            .With("approvals/1.0.0/processes/Review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "Review", "formatVersion": 1,
                  "entity": "Approval", "start": "review", "steps": [
                    { "name": "review", "type": "task", "label": { "textKey": "approvals.review" }, "assignee": {{assignee}}, "form": "ApprovalForm",
                      "outcomes": [ { "name": "approve", "label": { "textKey": "approvals.approve" }, "next": "done" } ]{{escalations}} },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.approval": "Approval", "approvals.review": "Review", "approvals.approve": "Approve", "approvals.approver": "Approver" } }
                """);
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
                  "texts": { "invoice.title": "Invoice", "audit.check": "Check", "role.manager": "Manager" } }
                """)
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "manager", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """)
            .With("entities/Invoice.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "entity", "name": "Invoice", "formatVersion": 1,
                  "fields": [ { "name": "number", "type": "text" } ] }
                """)
            .With("forms/InvoiceForm.json", """
                { "id": "44444444-4444-4444-8444-444444444412", "kind": "form", "name": "InvoiceForm", "formatVersion": 1, "entity": "Invoice",
                  "sections": [ { "title": { "textKey": "invoice.title" }, "fields": [ { "field": "number" } ] } ] }
                """);
    }
}
