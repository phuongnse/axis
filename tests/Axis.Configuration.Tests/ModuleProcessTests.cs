using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ModuleProcessTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ProcessId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41";
    private const string ProcessFile = "modules/approvals/processes/Review.json";

    [Fact]
    public void A_module_process_joins_the_model_under_its_module_with_a_derived_id()
    {
        using var modules = Modules();
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var process = Assert.Single(model.Processes);
            Assert.Equal("approvals/Review", process.Name);
            Assert.Equal(ProcessFile, process.File);
            Assert.Equal(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(ProcessId)), process.Id);
            Assert.NotEqual(Guid.Parse(ProcessId), process.Id);

            var entity = Assert.Single(model.Entities, candidate => candidate.Name == "approvals/Approval");
            Assert.Equal(new EntityReference(entity.Id, "approvals/Approval"), process.Entity);
        }
    }

    [Fact]
    public void A_module_task_that_names_a_form_of_another_module_is_reported_at_the_form()
    {
        using var modules = Modules();
        using var folder = Application();
        modules.With("approvals/1.0.0/processes/Review.json", Process(form: "other/Form"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, ProcessFile, "/steps/0/form", Guid.Parse(ProcessId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'other/Form'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_task_that_names_an_application_form_is_unknown_in_the_module_file()
    {
        using var modules = Modules();
        using var folder = Application();
        modules.With("approvals/1.0.0/processes/Review.json", Process(form: "InvoiceForm"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTaskForm, ProcessFile, "/steps/0/form"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_package_step_is_reported_with_the_module_manifest()
    {
        using var modules = Modules();
        using var folder = Application();
        modules.With("approvals/1.0.0/processes/Review.json", Process(
            extraStep: """{ "name": "call", "type": "operation", "operation": "somepkg/Do", "input": {}, "next": "review" },""",
            start: "call"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownStepPackage, ProcessFile, "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("module.json", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_page_can_inspect_a_process_of_its_module()
    {
        using var modules = Modules();
        using var folder = Application();
        modules.With("approvals/1.0.0/pages/Reviews.json", """
            { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c51", "kind": "page", "name": "Reviews", "formatVersion": 1,
              "title": { "textKey": "approvals.reviews" },
              "widgets": [ { "type": "processInspector", "process": "Review" } ] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var page = Assert.Single(compiled.Model!.Pages, candidate => candidate.Name == "approvals/Reviews");
        var processId = ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(ProcessId));
        Assert.Equal(
            new WidgetModel(WidgetType.ProcessInspector, null, Process: new ProcessReference(processId, "approvals/Review")),
            Assert.Single(page.Widgets));
    }

    [Fact]
    public void An_application_task_cannot_name_a_form_of_a_module_it_does_not_declare()
    {
        using var modules = Modules();
        using var folder = Application().With("processes/Audit.json", """
            { "id": "44444444-4444-4444-8444-444444444431", "kind": "process", "name": "Audit", "formatVersion": 1,
              "entity": "Invoice", "start": "check", "steps": [
                { "name": "check", "type": "task", "label": { "textKey": "audit.check" }, "assignee": { "user": "'u1'" }, "form": "departments/DepartmentForm",
                  "outcomes": [ { "name": "close", "label": { "textKey": "audit.close" }, "next": "done" } ] },
                { "name": "done", "type": "end" }
              ] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTaskForm, "processes/Audit.json", "/steps/0/form"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void An_application_trigger_action_and_sub_process_step_can_name_a_process_of_a_declared_module()
    {
        using var modules = Modules();
        using var folder = WithProcessReferences(Application(), "approvals/Review", dataSource: true);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var processId = ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(ProcessId));
        var reference = new ProcessReference(processId, "approvals/Review");
        Assert.Equal(reference, Assert.Single(compiled.Model!.Triggers).Process);
        var widget = Assert.Single(Assert.Single(compiled.Model.Pages, page => page.Name == "Reviews").Widgets);
        var action = Assert.IsType<StartProcessActionModel>(Assert.Single(widget.RowActions));
        Assert.Equal(reference, action.Process);
        var parent = Assert.Single(compiled.Model.Processes, process => process.Name == "Parent");
        var step = Assert.IsType<SubProcessStepModel>(parent.Steps[0]);
        Assert.Equal((processId, "approvals/Review"), (step.ProcessId, step.Process));
    }

    [Theory]
    [InlineData("other/Review")]
    [InlineData("departments/Onboard")]
    public void An_application_reference_to_a_process_of_a_module_it_does_not_declare_is_axc0207(string name)
    {
        using var modules = Modules();
        modules.With("departments/1.0.0/processes/Onboard.json", """
            { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c34", "kind": "process", "name": "Onboard", "formatVersion": 1,
              "entity": "Department", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
            """);
        using var folder = WithProcessReferences(Application(), name, dataSource: false);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "pages/Reviews.json", "/widgets/0/rowActions/0/process"),
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "processes/Parent.json", "/steps/0/process"),
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "triggers/Nightly.json", "/process"),
            ],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)).Order().ToArray());
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void An_endpoint_can_create_a_record_of_a_declared_module_and_start_its_process()
    {
        using var modules = Modules();
        using var folder = Application().With("endpoints/Import.json", Endpoint("approvals/Approval", "approvals/Review"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var endpoint = Assert.Single(compiled.Model!.Endpoints);
        var entityId = ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse("5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21"));
        var processId = ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(ProcessId));
        Assert.Equal(new EntityReference(entityId, "approvals/Approval"), endpoint.Entity);
        Assert.Equal(new ProcessReference(processId, "approvals/Review"), endpoint.Process);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("departments")]
    public void An_endpoint_that_names_an_entity_and_a_process_of_a_module_it_does_not_declare_is_axc0207(string module)
    {
        using var modules = Modules();
        modules.With("departments/1.0.0/processes/Onboard.json", """
            { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c34", "kind": "process", "name": "Onboard", "formatVersion": 1,
              "entity": "Department", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
            """);
        using var folder = Application().With("endpoints/Import.json", Endpoint($"{module}/Department", $"{module}/Onboard"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "endpoints/Import.json", "/entity"),
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "endpoints/Import.json", "/process"),
            ],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)).Order().ToArray());
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_process_inspector_that_names_a_process_of_an_undeclared_module_is_axc0207()
    {
        using var modules = Modules();
        using var folder = Application().With("pages/Inspect.json", """
            { "id": "44444444-4444-4444-8444-444444444461", "kind": "page", "name": "Inspect", "formatVersion": 1,
              "title": { "textKey": "invoice.title" },
              "widgets": [ { "type": "processInspector", "process": "departments/Onboard" } ] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, "pages/Inspect.json", "/widgets/0/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void An_application_policy_can_grant_a_process_of_a_declared_module()
    {
        using var modules = Modules();
        using var folder = Application()
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "manager", "formatVersion": 1, "label": { "textKey": "invoice.title" } }
                """)
            .With("policies/ReviewAccess.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "policy", "name": "ReviewAccess", "formatVersion": 1,
                  "roles": ["manager"], "grants": [ { "name": "reviewStart", "process": "approvals/Review", "actions": ["start"] } ] }
                """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var grant = Assert.Single(Assert.Single(compiled.Model!.Policies).Grants);
        Assert.Equal(new ProcessReference(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(ProcessId)), "approvals/Review"), grant.Process);
    }

    /// <summary>
    /// Adds a schedule trigger, a page with a row action and an application process with a sub-process step that each name
    /// <paramref name="name"/>. With <paramref name="dataSource"/>, the trigger and the page read the module's
    /// approvals through a data source, as a trigger that starts a process for each row needs.
    /// </summary>
    private static TemporaryFolder WithProcessReferences(TemporaryFolder folder, string name, bool dataSource)
    {
        var source = dataSource ? """ "dataSource": "Approvals", """ : "";
        return folder
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "invoice.title": "Invoice", "audit.check": "Check", "audit.close": "Close", "reviews.title": "Reviews", "reviews.start": "Start review" } }
                """)
            .With("data-sources/Approvals.json", """
                { "id": "44444444-4444-4444-8444-444444444421", "kind": "dataSource", "name": "Approvals", "formatVersion": 1,
                  "entity": "approvals/Approval", "fields": [ { "name": "title", "path": "title" } ] }
                """)
            .With("triggers/Nightly.json", $$"""
                { "id": "44444444-4444-4444-8444-444444444431", "kind": "trigger", "name": "Nightly", "formatVersion": 1,
                  "type": "schedule", "schedule": "0 6 * * *", "timeZone": "Europe/Berlin",{{source}} "process": "{{name}}" }
                """)
            .With("pages/Reviews.json", $$"""
                { "id": "44444444-4444-4444-8444-444444444441", "kind": "page", "name": "Reviews", "formatVersion": 1,
                  "title": { "textKey": "reviews.title" },
                  "widgets": [ { "type": "table", "dataSource": "Approvals",
                    "rowActions": [ { "type": "startProcess", "process": "{{name}}", "label": { "textKey": "reviews.start" } } ] } ] }
                """)
            .With("processes/Parent.json", $$"""
                { "id": "44444444-4444-4444-8444-444444444451", "kind": "process", "name": "Parent", "formatVersion": 1,
                  "entity": "approvals/Approval", "start": "run", "steps": [
                    { "name": "run", "type": "subProcess", "process": "{{name}}", "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """);
    }

    private static string Endpoint(string entity, string process) => $$"""
        { "id": "44444444-4444-4444-8444-444444444471", "kind": "endpoint", "name": "Import", "formatVersion": 1,
          "entity": "{{entity}}", "process": "{{process}}",
          "input": [ { "name": "title", "type": "text" } ], "values": { "title": "title" } }
        """;

    private static string Process(string form = "ApprovalForm", string extraStep = "", string start = "review") => $$"""
        { "id": "{{ProcessId}}", "kind": "process", "name": "Review", "formatVersion": 1,
          "entity": "Approval", "start": "{{start}}", "steps": [
            {{extraStep}}
            { "name": "review", "type": "task", "label": { "textKey": "approvals.review" }, "assignee": { "user": "'u1'" }, "form": "{{form}}",
              "outcomes": [ { "name": "approve", "label": { "textKey": "approvals.approve" }, "next": "done" } ] },
            { "name": "done", "type": "end" }
          ] }
        """;

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c31", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("departments/1.0.0/forms/DepartmentForm.json", """
                { "id": "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c33", "kind": "form", "name": "DepartmentForm", "formatVersion": 1, "entity": "Department",
                  "sections": [ { "title": { "textKey": "departments.department" }, "fields": [ { "field": "name" } ] } ] }
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
            .With("approvals/1.0.0/processes/Review.json", Process())
            .With("approvals/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "approvals.approval": "Approval", "approvals.review": "Review", "approvals.approve": "Approve", "approvals.reviews": "Reviews" } }
                """);

    private static TemporaryFolder Application() =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "approvals", "version": "1.0.0" }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "invoice.title": "Invoice", "audit.check": "Check", "audit.close": "Close" } }
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
