using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ModuleTriggerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e02";
    private const string OtherApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e03";
    private const string ApprovalId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c21";
    private const string DepartmentId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c31";
    private const string EventId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c32";
    private const string CloseId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c41";
    private const string NoteId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c42";
    private const string CloseTriggerId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c51";
    private const string NoteTriggerId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c52";
    private const string SweepId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c43";
    private const string ApprovalsDataSourceId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c61";
    private const string ScheduleTriggerId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53";
    private const string ScheduleTriggerFile = "modules/approvals/triggers/Nightly.json";
    private const string CloseTriggerFile = "modules/approvals/triggers/CloseNew.json";
    private const string NoteTriggerFile = "modules/approvals/triggers/NoteRenamed.json";

    [Fact]
    public void A_module_trigger_joins_the_model_under_its_module_with_a_derived_id()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(rebuilt.Diagnostics);
        foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
        {
            var application = Guid.Parse(ApplicationId);
            Assert.Equal(2, model.Triggers.Count);

            var close = Assert.Single(model.Triggers, trigger => trigger.Name == "approvals/CloseNew");
            Assert.Equal(CloseTriggerFile, close.File);
            Assert.Equal(ModuleResourceIds.Derive(application, Guid.Parse(CloseTriggerId)), close.Id);
            Assert.Equal(TriggerType.DataChange, close.Type);
            var approval = Assert.Single(model.Entities, entity => entity.Name == "approvals/Approval");
            Assert.Equal(new EntityReference(approval.Id, "approvals/Approval"), close.Entity);
            var closeProcess = Assert.Single(model.Processes, process => process.Name == "approvals/Close");
            Assert.Equal(new ProcessReference(closeProcess.Id, "approvals/Close"), close.Process);

            var note = Assert.Single(model.Triggers, trigger => trigger.Name == "approvals/NoteRenamed");
            Assert.Equal(ModuleResourceIds.Derive(application, Guid.Parse(NoteTriggerId)), note.Id);
            Assert.Equal(TriggerType.Event, note.Type);
            var @event = Assert.Single(model.Events, candidate => candidate.Name == "departments/DepartmentRenamed");
            Assert.Equal(new EventReference(@event.Id, "departments/DepartmentRenamed"), note.Event);
            var noteProcess = Assert.Single(model.Processes, process => process.Name == "approvals/Note");
            Assert.Equal(new ProcessReference(noteProcess.Id, "approvals/Note"), note.Process);
        }
    }

    [Fact]
    public void Two_applications_get_different_ids_for_the_same_module_trigger()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId);
        using var other = Application(OtherApplicationId);
        var installed = ModuleCatalog.Load(modules.Path).Modules;

        var first = ApplicationCompiler.Compile(folder.Path, null, installed);
        var second = ApplicationCompiler.Compile(other.Path, null, installed);

        Assert.Empty(first.Diagnostics);
        Assert.Empty(second.Diagnostics);
        Assert.Equal(
            first.Model!.Triggers.Select(trigger => trigger.Name).Order().ToArray(),
            second.Model!.Triggers.Select(trigger => trigger.Name).Order().ToArray());
        Assert.Empty(first.Model.Triggers.Select(trigger => trigger.Id).Intersect(second.Model.Triggers.Select(trigger => trigger.Id)));
    }

    [Fact]
    public void A_module_trigger_that_names_an_application_process_is_unknown_in_the_module_file()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId).With("processes/Audit.json", """
            { "id": "44444444-4444-4444-8444-444444444431", "kind": "process", "name": "Audit", "formatVersion": 1,
              "entity": "Invoice", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
            """);
        modules.With("approvals/1.0.0/triggers/CloseNew.json", CloseTrigger(process: "Audit"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTriggerProcess, CloseTriggerFile, "/process", Guid.Parse(CloseTriggerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_trigger_that_names_its_process_with_a_slash_is_reported_once_at_the_process()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId);
        modules.With("approvals/1.0.0/triggers/CloseNew.json", CloseTrigger(process: "approvals/Close"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, CloseTriggerFile, "/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_trigger_that_names_the_event_of_a_module_it_does_not_declare_is_reported_at_the_event()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId);
        modules.With("approvals/1.0.0/triggers/NoteRenamed.json", NoteTrigger(@event: "other/DepartmentRenamed"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, NoteTriggerFile, "/event"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_trigger_that_names_an_unknown_event_or_entity_keeps_its_usual_code_in_the_module_file()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId);
        modules.With("approvals/1.0.0/triggers/NoteRenamed.json", NoteTrigger(@event: "departments/Missing"));
        modules.With("approvals/1.0.0/triggers/CloseNew.json", CloseTrigger(entity: "Missing"));

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownTriggerEvent, NoteTriggerFile, "/event"),
                (DiagnosticCodes.UnknownTriggerEntity, CloseTriggerFile, "/entity"),
            ],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)).Order().ToArray());
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_schedule_trigger_runs_in_the_time_zone_of_each_application_with_its_own_id()
    {
        using var modules = Modules().With("approvals/1.0.0/triggers/Nightly.json", ScheduleTrigger(dataSource: "Approvals"));
        using var folder = Application(ApplicationId, "Asia/Ho_Chi_Minh");
        using var other = Application(OtherApplicationId, "Europe/Berlin");
        var installed = ModuleCatalog.Load(modules.Path).Modules;

        foreach (var (applicationFolder, applicationId, timeZone) in new[]
        {
            (folder, ApplicationId, "Asia/Ho_Chi_Minh"),
            (other, OtherApplicationId, "Europe/Berlin"),
        })
        {
            var compiled = ApplicationCompiler.Compile(applicationFolder.Path, null, installed);
            var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

            Assert.Empty(compiled.Diagnostics);
            Assert.Empty(rebuilt.Diagnostics);
            foreach (var model in new[] { compiled.Model!, rebuilt.Model! })
            {
                var nightly = Assert.Single(model.Triggers, trigger => trigger.Name == "approvals/Nightly");
                Assert.Equal(ScheduleTriggerFile, nightly.File);
                Assert.Equal(ModuleResourceIds.Derive(Guid.Parse(applicationId), Guid.Parse(ScheduleTriggerId)), nightly.Id);
                Assert.Equal(TriggerType.Schedule, nightly.Type);
                Assert.Equal(timeZone, nightly.TimeZone!.Id);
                var dataSource = Assert.Single(model.DataSources, candidate => candidate.Name == "approvals/Approvals");
                Assert.Equal(new DataSourceReference(dataSource.Id, "approvals/Approvals"), nightly.DataSource);
                var process = Assert.Single(model.Processes, candidate => candidate.Name == "approvals/Close");
                Assert.Equal(new ProcessReference(process.Id, "approvals/Close"), nightly.Process);
            }
        }
    }

    [Fact]
    public void A_module_schedule_trigger_without_a_data_source_is_a_scheduled_job()
    {
        using var modules = Modules()
            .With("approvals/1.0.0/processes/Sweep.json", $$"""
                { "id": "{{SweepId}}", "kind": "process", "name": "Sweep", "formatVersion": 1,
                  "parameters": [ { "name": "days", "type": "integer" } ],
                  "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """)
            .With("approvals/1.0.0/triggers/Nightly.json", ScheduleTrigger(process: "Sweep", parameters: """{ "days": 3 }"""));
        using var folder = Application(ApplicationId, "Europe/Berlin");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        var nightly = Assert.Single(compiled.Model!.Triggers, trigger => trigger.Name == "approvals/Nightly");
        Assert.Null(nightly.DataSource);
        Assert.Equal("Europe/Berlin", nightly.TimeZone!.Id);
    }

    [Fact]
    public void A_module_schedule_trigger_without_an_application_time_zone_runs_in_utc()
    {
        using var modules = Modules().With("approvals/1.0.0/triggers/Nightly.json", ScheduleTrigger(dataSource: "Approvals"));
        using var folder = Application(ApplicationId);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(TimeZoneInfo.Utc, Assert.Single(compiled.Model!.Triggers, trigger => trigger.Name == "approvals/Nightly").TimeZone);
    }

    [Fact]
    public void A_module_schedule_trigger_with_a_time_zone_is_a_schema_error_in_the_module_file()
    {
        using var modules = Modules().With("approvals/1.0.0/triggers/Nightly.json", ScheduleTrigger(dataSource: "Approvals", timeZone: "Europe/Berlin"));
        using var folder = Application(ApplicationId);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, ScheduleTriggerFile, "/timeZone", Guid.Parse(ScheduleTriggerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void A_module_schedule_trigger_that_names_an_application_data_source_is_unknown_in_the_module_file()
    {
        using var modules = Modules().With("approvals/1.0.0/triggers/Nightly.json", ScheduleTrigger(dataSource: "Invoices"));
        using var folder = Application(ApplicationId).With("data-sources/Invoices.json", """
            { "id": "44444444-4444-4444-8444-444444444433", "kind": "dataSource", "name": "Invoices", "formatVersion": 1,
              "entity": "Invoice", "fields": [ { "name": "number", "path": "number" } ] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTriggerDataSource, ScheduleTriggerFile, "/dataSource", Guid.Parse(ScheduleTriggerId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void An_application_trigger_still_cannot_name_a_process_of_a_module_it_does_not_declare()
    {
        using var modules = Modules();
        using var folder = Application(ApplicationId).With("triggers/Other.json", """
            { "id": "44444444-4444-4444-8444-444444444432", "kind": "trigger", "name": "Other", "formatVersion": 1,
              "type": "dataChange", "entity": "Invoice", "on": ["create"], "process": "other/Close" }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, "triggers/Other.json", "/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private static string CloseTrigger(string process = "Close", string entity = "Approval") => $$"""
        { "id": "{{CloseTriggerId}}", "kind": "trigger", "name": "CloseNew", "formatVersion": 1,
          "type": "dataChange", "entity": "{{entity}}", "on": ["create"], "process": "{{process}}" }
        """;

    private static string ScheduleTrigger(string process = "Close", string? dataSource = null, string? timeZone = null, string? parameters = null) => $$"""
        { "id": "{{ScheduleTriggerId}}", "kind": "trigger", "name": "Nightly", "formatVersion": 1,
          "type": "schedule", "schedule": "0 6 * * *", "process": "{{process}}"
          {{(dataSource is null ? "" : $", \"dataSource\": \"{dataSource}\"")}}
          {{(timeZone is null ? "" : $", \"timeZone\": \"{timeZone}\"")}}
          {{(parameters is null ? "" : $", \"parameters\": {parameters}")}} }
        """;

    private static string NoteTrigger(string @event = "departments/DepartmentRenamed") => $$"""
        { "id": "{{NoteTriggerId}}", "kind": "trigger", "name": "NoteRenamed", "formatVersion": 1,
          "type": "event", "event": "{{@event}}", "mode": "async", "process": "Note" }
        """;

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", $$"""
                { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("departments/1.0.0/DepartmentRenamed.json", $$"""
                { "id": "{{EventId}}", "kind": "event", "name": "DepartmentRenamed", "formatVersion": 1, "entity": "Department",
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("approvals/1.0.0/module.json", """
                { "kind": "module", "id": "approvals", "version": "1.0.0", "label": "Approvals",
                  "modules": [ { "id": "departments", "version": "^1.0.0" } ] }
                """)
            .With("approvals/1.0.0/Approval.json", $$"""
                { "id": "{{ApprovalId}}", "kind": "entity", "name": "Approval", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" }, { "name": "state", "type": "text" } ] }
                """)
            .With("approvals/1.0.0/data-sources/Approvals.json", $$"""
                { "id": "{{ApprovalsDataSourceId}}", "kind": "dataSource", "name": "Approvals", "formatVersion": 1,
                  "entity": "Approval", "fields": [ { "name": "title", "path": "title" } ] }
                """)
            .With("approvals/1.0.0/processes/Close.json", $$"""
                { "id": "{{CloseId}}", "kind": "process", "name": "Close", "formatVersion": 1,
                  "entity": "Approval", "start": "close", "steps": [
                    { "name": "close", "type": "operation", "operation": "updateRecord", "set": { "state": "'closed'" }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With("approvals/1.0.0/processes/Note.json", $$"""
                { "id": "{{NoteId}}", "kind": "process", "name": "Note", "formatVersion": 1,
                  "entity": "departments/Department", "start": "note", "steps": [
                    { "name": "note", "type": "operation", "operation": "updateRecord", "set": { "name": "'noted'" }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """)
            .With("approvals/1.0.0/triggers/CloseNew.json", CloseTrigger())
            .With("approvals/1.0.0/triggers/NoteRenamed.json", NoteTrigger());

    private static TemporaryFolder Application(string id, string? timeZone = null) =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{id}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  {{(timeZone is null ? "" : $"\"timeZone\": \"{timeZone}\",")}}
                  "modules": [{ "id": "approvals", "version": "1.0.0" }] }
                """)
            .With("entities/Invoice.json", """
                { "id": "44444444-4444-4444-8444-444444444411", "kind": "entity", "name": "Invoice", "formatVersion": 1,
                  "fields": [ { "name": "number", "type": "text" } ] }
                """);
}
