using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Sql;

namespace Axis.Configuration.Tests;

public sealed class ModuleCompilerTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string DepartmentId = "5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c07";

    private const string Department = $$"""
        { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string SequenceId = "7e2a4b90-3c1d-4f6e-8a0b-9d5c1e2f3a04";
    private const string SeedId = "9b4c6d12-5e3f-4a70-8c2d-1f6e3a4b5c08";
    private const string SeedRecordId = "3a5b7c9d-1e2f-4a3b-9c4d-5e6f7a8b9c0d";
    private const string PersonRequest = $$"""
        { "id": "a1b2c3d4-0000-4000-8000-000000000001", "kind": "entity", "name": "Request", "formatVersion": 1,
          "fields": [ { "name": "department", "type": "reference", "target": "{{DepartmentName}}" } ] }
        """;
    private const string DepartmentName = "departments/Department";

    private const string EventId = "4d6e8f10-2a3b-4c5d-9e6f-7a8b9c0d1e2f";
    private const string DepartmentRenamed = $$"""
        { "id": "{{EventId}}", "kind": "event", "name": "DepartmentRenamed", "formatVersion": 1, "entity": "Department",
          "fields": [ { "name": "name", "type": "text" } ] }
        """;
    private const string RenamedProcess = """
        { "id": "6f8a0b12-3c4d-4e5f-8a6b-9c0d1e2f3a4b", "kind": "process", "name": "RenameReview", "formatVersion": 1,
          "entity": "departments/Department", "start": "announce",
          "steps": [
            { "name": "announce", "type": "operation", "operation": "raiseEvent", "event": "departments/DepartmentRenamed",
              "fields": { "name": "name" }, "next": "done" },
            { "name": "done", "type": "end" }
          ] }
        """;

    private const string NamedDepartment = $$"""
        { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1, "displayField": "name",
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string DataSourceId = "99999999-9999-4999-8999-999999999902";
    private const string FormId = "88888888-8888-4888-8888-888888888802";
    private const string DepartmentsDataSourceName = "departments/Departments";

    private const string ModuleDataSource = $$"""
        { "id": "{{DataSourceId}}", "kind": "dataSource", "name": "Departments", "formatVersion": 1,
          "entity": "Department", "fields": [ { "name": "name", "path": "name" } ] }
        """;

    private const string ModuleForm = $$"""
        { "id": "{{FormId}}", "kind": "form", "name": "DepartmentLayout", "formatVersion": 1, "entity": "Department",
          "sections": [ { "title": { "textKey": "department.main" }, "fields": [ { "field": "name" } ] } ] }
        """;

    private const string DepartmentsPage = """
        { "id": "66666666-6666-4666-8666-666666666601", "kind": "page", "name": "Departments", "formatVersion": 1,
          "title": { "textKey": "pages.departments.title" },
          "widgets": [ { "type": "table", "dataSource": "departments/Departments" } ] }
        """;

    private const string DepartmentsPolicy = """
        { "id": "12121212-1212-4212-8212-121212121202", "kind": "policy", "name": "DepartmentsAccess", "formatVersion": 1,
          "roles": ["employee"], "grants": [ { "dataSource": "departments/Departments", "actions": ["read"] } ] }
        """;

    private const string LabelledDepartment = $$"""
        { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1, "label": { "textKey": "departments.department" },
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string ApplicationTextsEnId = "b7d1e3f5-2a4c-4e6f-8b9a-0c1d2e3f4a5b";
    private const string ModuleTextsEnId = "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c";
    private const string ModuleTextsViId = "d9f3a5b7-4c6e-4a81-8d1c-2e3f4a5b6c7d";

    [Fact]
    public void A_module_entity_joins_the_model_with_an_id_derived_from_the_application_and_a_qualified_name()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = Folder("""[{ "id": "departments", "version": "^1.0.0" }]""");
        var installed = ModuleCatalog.Load(modules.Path).Modules;

        var compiled = ApplicationCompiler.Compile(folder.Path, null, installed);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        var entity = Assert.Single(compiled.Model!.Entities);
        var expected = ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(DepartmentId));
        Assert.Equal((expected, DepartmentName, "modules/departments/Department.json"), (entity.Id, entity.Name, entity.File));
        Assert.NotEqual(Guid.Parse(DepartmentId), entity.Id);
        Assert.Equal(expected, Assert.Single(rebuilt.Model!.Entities).Id);
    }

    [Fact]
    public void A_module_entity_id_stays_the_same_when_another_version_is_pinned()
    {
        using var first = Modules("1.0.0");
        using var second = Modules("1.2.0");
        using var folderOne = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");
        using var folderTwo = Folder("""[{ "id": "departments", "version": "1.2.0" }]""");

        var one = ApplicationCompiler.Compile(folderOne.Path, null, ModuleCatalog.Load(first.Path).Modules);
        var two = ApplicationCompiler.Compile(folderTwo.Path, null, ModuleCatalog.Load(second.Path).Modules);

        Assert.Equal(Assert.Single(one.Model!.Entities).Id, Assert.Single(two.Model!.Entities).Id);
    }

    [Fact]
    public void Two_applications_get_different_ids_for_the_same_module_entity()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");
        using var other = new TemporaryFolder().With("application.json", $$"""
            { "id": "1f2e3d4c-5b6a-4788-9a0b-1c2d3e4f5a6b", "kind": "application", "name": "Other", "formatVersion": 1,
              "modules": [{ "id": "departments", "version": "1.0.0" }] }
            """);
        var installed = ModuleCatalog.Load(modules.Path).Modules;

        var first = ApplicationCompiler.Compile(folder.Path, null, installed);
        var second = ApplicationCompiler.Compile(other.Path, null, installed);

        Assert.NotEqual(Assert.Single(first.Model!.Entities).Id, Assert.Single(second.Model!.Entities).Id);
    }

    [Fact]
    public void The_UUID_v5_helper_matches_the_RFC_9562_example()
    {
        var dnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), ModuleResourceIds.Create(dnsNamespace, "www.example.com"));
    }

    [Fact]
    public void An_application_field_can_target_a_module_entity()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/Department.json", NamedDepartment);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""").With("Request.json", PersonRequest);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var request = result.Model!.Entities.Single(entity => entity.Name == "Request");
        var target = result.Model.Entities.Single(entity => entity.Name == DepartmentName);
        var field = Assert.Single(request.Fields);
        Assert.Equal(new EntityReference(target.Id, DepartmentName), field.Target);
        Assert.Equal(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(DepartmentId)), field.Target!.Id);
        Assert.Equal("name", field.TargetDisplayField);
        Assert.Equal([DepartmentName, "Request"], result.Model.Entities.Select(entity => entity.Name));
    }

    [Fact]
    public void An_application_data_source_form_and_policy_grant_can_name_a_module_entity()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/Department.json", NamedDepartment)
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("texts/en.json", EnglishTexts)
            .With("data-sources/departments.json", DataSource(DepartmentName))
            .With("forms/department.json", FormOver(DepartmentName))
            .With("roles/employee.json", Role)
            .With("policies/departments.json", PolicyOver(DepartmentName));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var expected = ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(DepartmentId));
        var model = result.Model!;
        Assert.Equal(new EntityReference(expected, DepartmentName), Assert.Single(model.DataSources).Entity);
        Assert.Equal(new EntityReference(expected, DepartmentName), Assert.Single(model.Forms).Entity);
        var grant = Assert.Single(Assert.Single(model.Policies).Grants);
        Assert.Equal(new EntityReference(expected, DepartmentName), grant.Entity);
    }

    [Fact]
    public void A_migration_that_names_a_module_entity_is_reported_at_its_entity()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("migrations/0001.json", """
                { "id": "2b4c6d8e-1f3a-4b5c-9d7e-0a1b2c3d4e5f", "kind": "migration", "name": "DropName", "formatVersion": 1,
                  "entity": "departments/Department", "changes": [ { "type": "dropField", "field": "name" } ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MigrationOfModuleEntity, "migrations/0001.json", "/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_data_source_and_a_form_over_an_undeclared_module_are_reported_at_the_entity()
    {
        using var folder = Folder("[]")
            .With("texts/en.json", EnglishTexts)
            .With("data-sources/departments.json", DataSource("other/Department"))
            .With("forms/department.json", FormOver("other/Department"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "data-sources/departments.json", "/entity"),
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "forms/department.json", "/entity"),
            ],
            result.Diagnostics
                .Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path))
                .OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_data_source_over_a_name_the_pinned_module_lacks_keeps_the_unknown_entity_code()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("data-sources/departments.json", DataSource("departments/Missing"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownDataSourceEntity, "data-sources/departments.json", "/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_data_source_under_a_module_that_could_not_be_pinned_is_not_reported_again()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "^2.0.0" }]""")
            .With("data-sources/departments.json", DataSource(DepartmentName));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(DiagnosticCodes.NoModuleVersionInRange, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void A_reference_to_an_undeclared_module_is_reported_at_the_target()
    {
        using var folder = Folder("[]").With("Request.json", PersonRequest.Replace(DepartmentName, "people/Person", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, "Request.json", "/fields/0/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'people'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_name_the_pinned_module_lacks_keeps_the_unknown_entity_code()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("Request.json", PersonRequest.Replace(DepartmentName, "departments/Missing", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownReferenceTarget, "Request.json", "/fields/0/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_reference_under_a_module_that_could_not_be_pinned_is_not_reported_again()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "^2.0.0" }]""").With("Request.json", PersonRequest);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(DiagnosticCodes.NoModuleVersionInRange, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void A_module_entity_field_that_names_another_module_with_a_slash_is_reported_once()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/Employee.json",
            """
            { "id": "8a0b2e44-1c3d-4e5f-8a6b-7c8d9e0f1a2b", "kind": "entity", "name": "Employee", "formatVersion": 1,
              "fields": [ { "name": "person", "type": "reference", "target": "people/Person" } ] }
            """);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, "modules/departments/Employee.json", "/fields/0/target", Guid.Parse("8a0b2e44-1c3d-4e5f-8a6b-7c8d9e0f1a2b")),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_entity_field_names_a_resource_of_its_own_module_without_a_slash()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/Department.json", NamedDepartment)
            .With(
                "departments/1.0.0/Employee.json",
                """
                { "id": "8a0b2e44-1c3d-4e5f-8a6b-7c8d9e0f1a2b", "kind": "entity", "name": "Employee", "formatVersion": 1,
                  "fields": [ { "name": "department", "type": "reference", "target": "Department" } ] }
                """);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var employee = result.Model!.Entities.Single(entity => entity.Name == "departments/Employee");
        Assert.Equal(DepartmentName, Assert.Single(employee.Fields).Target!.Name);
    }

    [Fact]
    public void A_module_sequence_and_seed_join_the_model_with_derived_ids_and_qualified_names()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/Department.json", $$"""
                { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "code", "type": "text", "sequence": "Codes" } ] }
                """)
            .With("departments/1.0.0/Codes.json", $$"""
                { "id": "{{SequenceId}}", "kind": "sequence", "name": "Codes", "formatVersion": 1, "format": "D-{n:4}" }
                """)
            .With("departments/1.0.0/Departments.json", $$"""
                { "id": "{{SeedId}}", "kind": "seed", "name": "Departments", "formatVersion": 1, "entity": "Department",
                  "records": [ { "id": "{{SeedRecordId}}", "values": { "code": "D-0001" } } ] }
                """);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var applicationId = Guid.Parse(ApplicationId);
        var entity = Assert.Single(result.Model!.Entities);
        var sequence = Assert.Single(entity.Fields).Sequence!;
        Assert.Equal(("departments/Codes", ModuleResourceIds.Derive(applicationId, Guid.Parse(SequenceId))), (sequence.Name, sequence.Id));
        var seed = Assert.Single(result.Model.Seeds);
        Assert.Equal(
            ("departments/Departments", ModuleResourceIds.Derive(applicationId, Guid.Parse(SeedId))),
            (seed.Name, seed.Id));
        Assert.Equal(new EntityReference(entity.Id, "departments/Department"), seed.Entity);
        Assert.Equal(Guid.Parse(SeedRecordId), Assert.Single(seed.Records).Id);
    }

    [Fact]
    public void A_module_event_joins_the_model_with_a_derived_id_and_a_qualified_name()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/DepartmentRenamed.json", DepartmentRenamed);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var @event = Assert.Single(result.Model!.Events);
        Assert.Equal(
            (ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(EventId)), "departments/DepartmentRenamed", "modules/departments/DepartmentRenamed.json"),
            (@event.Id, @event.Name, @event.File));
        Assert.NotEqual(Guid.Parse(EventId), @event.Id);
    }

    [Fact]
    public void A_module_event_names_an_entity_of_its_own_module_without_a_slash()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/DepartmentRenamed.json", DepartmentRenamed);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var department = Assert.Single(result.Model!.Entities);
        Assert.Equal(new EntityReference(department.Id, DepartmentName), Assert.Single(result.Model.Events).Entity);
        Assert.Equal(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(DepartmentId)), department.Id);
    }

    [Fact]
    public void The_names_list_a_module_event_with_its_own_module_entity_qualified()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/DepartmentRenamed.json", DepartmentRenamed);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(DepartmentName, Assert.Single(result.Names.Events).Entity);
    }

    [Fact]
    public void A_module_event_whose_entity_names_another_module_with_a_slash_is_reported_once()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/DepartmentRenamed.json",
            DepartmentRenamed.Replace("\"entity\": \"Department\"", "\"entity\": \"people/Person\"", StringComparison.Ordinal));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, "modules/departments/DepartmentRenamed.json", "/entity", Guid.Parse(EventId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_application_process_over_a_module_entity_can_raise_a_module_event()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/DepartmentRenamed.json", DepartmentRenamed);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""").With("processes/rename-review.json", RenamedProcess);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var process = Assert.Single(result.Model!.Processes);
        Assert.Equal(DepartmentName, process.Entity!.Name);
        var raise = Assert.IsType<RaiseEventStepModel>(process.Steps[0]);
        Assert.Equal("departments/DepartmentRenamed", raise.Event);
    }

    [Fact]
    public void An_application_event_trigger_can_name_a_module_event()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/DepartmentRenamed.json", DepartmentRenamed);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("processes/rename-review.json", RenamedProcess)
            .With("triggers/on-rename.json", """
                { "id": "7a9b1c23-4d5e-4f60-8b7c-0d1e2f3a4b5c", "kind": "trigger", "name": "ReviewRenames", "formatVersion": 1,
                  "type": "event", "event": "departments/DepartmentRenamed", "process": "RenameReview" }
                """);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var trigger = Assert.Single(result.Model!.Triggers);
        Assert.Equal(
            new EventReference(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(EventId)), "departments/DepartmentRenamed"),
            trigger.Event);
    }

    [Fact]
    public void A_module_field_naming_a_missing_sequence_of_its_module_is_reported_in_the_module_file()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/Department.json", $$"""
            { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
              "fields": [ { "name": "code", "type": "text", "sequence": "Codes" } ] }
            """);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownSequence, "modules/departments/Department.json", "/fields/0/sequence", Guid.Parse(DepartmentId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
    }

    [Fact]
    public void A_caret_range_pins_the_highest_installed_version_and_stores_its_files()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = Folder("""[{ "id": "departments", "version": "^1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.Equal([new PinnedModuleModel("departments", "^1.0.0", "1.2.0")], result.Model!.Modules);
        Assert.Equal("^1.0.0", Assert.Single(result.Model.Manifest.Modules).Version);
        Assert.Equal(
            ["application.json", "modules/departments/Department.json", "modules/departments/module.json"],
            result.Resources.Select(resource => resource.Path));
        Assert.Contains("\"version\":\"1.2.0\"", result.Resources.Single(resource => resource.Path == "modules/departments/module.json").Content, StringComparison.Ordinal);
        Assert.Equal(ContentHash.Compute(result.Resources), result.ContentHash);
    }

    [Theory]
    [InlineData("~1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("^1.1.0", "1.2.0")]
    [InlineData("^1.0.0", "1.2.0")]
    public void Each_range_form_pins_the_highest_installed_version_in_it(string range, string pinned)
    {
        using var modules = Modules("1.0.0", "1.0.1", "1.2.0");
        using var folder = Folder($$"""[{ "id": "departments", "version": "{{range}}" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(pinned, Assert.Single(result.Model!.Modules).Version);
    }

    [Fact]
    public void A_newer_version_in_range_gives_a_new_hash_and_the_same_folder_keeps_its_own_for_the_same_versions()
    {
        using var folder = Folder("""[{ "id": "departments", "version": "^1.0.0" }]""");
        using var first = Modules("1.0.0", "1.2.0");
        using var second = Modules("1.0.0", "1.2.0", "1.3.0");

        var before = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(first.Path).Modules);
        var again = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(first.Path).Modules);
        var after = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(second.Path).Modules);

        Assert.Equal(before.ContentHash, again.ContentHash);
        Assert.NotEqual(before.ContentHash, after.ContentHash);
        Assert.Equal("1.3.0", Assert.Single(after.Model!.Modules).Version);
    }

    [Fact]
    public void Formatting_only_changes_to_a_module_file_keep_the_hash()
    {
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");
        using var compact = Modules("1.0.0");
        using var spaced = new TemporaryFolder()
            .With("departments/1.0.0/module.json", ModuleCatalogTests.Manifest("departments", "1.0.0"))
            .With("departments/1.0.0/Department.json", Department.Replace("\"kind\"", "\n\n  \"kind\"", StringComparison.Ordinal));

        var left = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(compact.Path).Modules);
        var right = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(spaced.Path).Modules);

        Assert.Equal(left.ContentHash, right.ContentHash);
    }

    [Fact]
    public void An_application_without_modules_has_the_hash_of_its_resources_alone()
    {
        using var modules = Modules("1.0.0");
        using var folder = new TemporaryFolder().With("application.json", PresentationCompilerTests.Manifest);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Model!.Modules);
        Assert.Equal(["application.json"], result.Resources.Select(resource => resource.Path));
        Assert.Equal(ContentHash.Compute(result.Resources), result.ContentHash);
    }

    [Fact]
    public void Resources_compiled_in_memory_give_the_same_hash_and_pins_without_the_installed_modules()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = Folder("""[{ "id": "departments", "version": "^1.0.0" }]""");
        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal(compiled.ContentHash, rebuilt.ContentHash);
        Assert.Equal(compiled.Model!.Modules, rebuilt.Model!.Modules);
        Assert.Equal(compiled.Resources, rebuilt.Resources);
    }

    [Fact]
    public void A_copy_in_memory_without_a_valid_manifest_is_reported_at_that_manifest()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");
        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var missing = ApplicationCompiler.Compile([.. compiled.Resources.Where(resource => resource.Path != "modules/departments/module.json")]);
        var invalid = ApplicationCompiler.Compile([.. compiled.Resources.Select(resource =>
            resource.Path == "modules/departments/module.json" ? resource with { Content = "{" } : resource)]);

        foreach (var result in new[] { missing, invalid })
        {
            Assert.Contains(
                result.Diagnostics,
                diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)
                    == (DiagnosticCodes.ModuleManifestInvalid, "modules/departments/module.json", ""));
            Assert.Null(result.Model);
        }
    }

    [Fact]
    public void A_copy_in_memory_whose_manifest_id_differs_from_its_folder_is_reported_at_id()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");
        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var result = ApplicationCompiler.Compile([.. compiled.Resources.Select(resource =>
            resource.Path == "modules/departments/module.json" ? resource with { Content = ModuleCatalogTests.Manifest("people", "1.0.0") } : resource)]);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)
                == (DiagnosticCodes.ModuleFolderMismatch, "modules/departments/module.json", "/id"));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_path_that_appears_twice_in_memory_is_rejected()
    {
        var resources = new[] { new ResourceContent("modules/departments/module.json", "{}"), new ResourceContent("modules/departments/module.json", "{}") };

        Assert.Throws<ArgumentException>(() => ApplicationCompiler.Compile(resources));
    }

    [Fact]
    public void A_range_that_no_installed_version_meets_is_reported_at_its_version_with_the_installed_versions()
    {
        using var modules = Modules("1.0.0", "1.2.0");
        using var folder = Folder("""[{ "id": "departments", "version": "^2.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.NoModuleVersionInRange, "application.json", "/modules/0/version", Guid.Parse(ApplicationId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'1.0.0', '1.2.0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_with_no_installed_version_is_unknown_at_its_id()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "people", "version": "^1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModule, "application.json", "/modules/0/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'people'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_installed_modules_every_module_is_unknown()
    {
        using var folder = Folder("""[{ "id": "departments", "version": "^1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownModule, "/modules/0/id"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("1.x")]
    [InlineData("1.0")]
    [InlineData(">=1.0.0")]
    [InlineData("^01.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData(" 1.0.0")]
    public void A_range_in_another_form_is_invalid_at_its_version(string range)
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder($$"""[{ "id": "departments", "version": "{{range}}" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidModuleRange, "application.json", "/modules/0/version"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'^1.2.0'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_id_listed_twice_is_reported_at_the_later_id_only()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""
            [{ "id": "departments", "version": "^1.0.0" }, { "id": "departments", "version": "nonsense" }]
            """);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateApplicationModule, "application.json", "/modules/1/id"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Theory]
    [InlineData("""[{ "id": "Departments", "version": "^1.0.0" }]""", "/modules/0/id")]
    [InlineData("""[{ "id": "departments" }]""", "/modules/0")]
    [InlineData("""[{ "id": "departments", "version": "" }]""", "/modules/0/version")]
    [InlineData("""[{ "id": "departments", "version": "^1.0.0", "url": "x" }]""", "/modules/0/url")]
    public void A_malformed_entry_is_a_schema_violation(string entries, string path)
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder(entries);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation && diagnostic.Path == path);
    }

    [Theory]
    [InlineData("""{ "kind": "endpoint" }""")]
    [InlineData("""{ "kind": "role" }""")]
    [InlineData("""{ "kind": "identityProvider" }""")]
    [InlineData("""{ "kind": "webhook" }""")]
    [InlineData("""{ "kind": "layer" }""")]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Nested" }""")]
    [InlineData("""{ "kind": "unknown" }""")]
    [InlineData("""{ "kind": 5 }""")]
    [InlineData("""{ "name": "NoKind" }""")]
    [InlineData("""[]""")]
    public void A_file_of_a_kind_a_module_may_not_hold_is_reported_at_its_kind(string content)
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/Other.json", content);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleKindNotAllowed, "modules/departments/Other.json", "/kind"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_file_with_a_resource_id_reports_it_in_the_diagnostic()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/Other.json",
            """{ "id": "8a0b2e44-1c3d-4e5f-8a6b-7c8d9e0f1a2b", "kind": "process" }""");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(Guid.Parse("8a0b2e44-1c3d-4e5f-8a6b-7c8d9e0f1a2b"), Assert.Single(result.Diagnostics).ResourceId);
    }

    [Fact]
    public void A_file_that_fails_the_schema_of_its_kind_is_a_schema_violation_in_the_module_file()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/Broken.json",
            """{ "id": "8a0b2e44-1c3d-4e5f-8a6b-7c8d9e0f1a2b", "kind": "entity", "name": "Broken", "formatVersion": 1 }""");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => (diagnostic.Code, diagnostic.File) == (DiagnosticCodes.SchemaViolation, "modules/departments/Broken.json"));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_file_that_is_not_JSON_is_reported_in_the_module_file()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/Broken.json", "{");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidJson, "modules/departments/Broken.json", ""),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_file_of_a_module_the_application_does_not_declare_is_not_checked()
    {
        using var modules = Modules("1.0.0").With("departments/1.0.0/Other.json", """{ "kind": "process" }""");
        using var folder = new TemporaryFolder().With("application.json", PresentationCompilerTests.Manifest);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void An_application_file_under_modules_is_reported_at_that_file_and_left_out()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("modules/departments/Department.json", Department)
            .With("modules/Other.json", Department);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (DiagnosticCodes.ReservedModulesFolder, "modules/Other.json", ""),
                (DiagnosticCodes.ReservedModulesFolder, "modules/departments/Department.json", ""),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_application_locale_without_a_module_text_is_reported_at_the_module_entry()
    {
        using var modules = Modules("1.0.0");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("TextsEn.json", Texts(ApplicationTextsEnId, "en", """{ "sample.title": "Sample" }"""));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingModuleLocale, "application.json", "/modules/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'en'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_text_key_without_the_module_prefix_is_reported_in_the_module_file()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "department": "Department" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleTextKeyWithoutPrefix, "modules/departments/TextsEn.json", "/texts/department", Guid.Parse(ModuleTextsEnId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_data_source_and_form_join_the_model_with_derived_ids_and_qualified_names()
    {
        using var modules = ModulesWithTexts()
            .With("departments/1.0.0/data-sources/Departments.json", ModuleDataSource)
            .With("departments/1.0.0/forms/DepartmentLayout.json", ModuleForm);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""").With("texts/en.json", EnglishTexts);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var applicationId = Guid.Parse(ApplicationId);
        var department = Assert.Single(result.Model!.Entities);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.Equal(
            (ModuleResourceIds.Derive(applicationId, Guid.Parse(DataSourceId)), DepartmentsDataSourceName, "modules/departments/data-sources/Departments.json"),
            (dataSource.Id, dataSource.Name, dataSource.File));
        Assert.Equal(new EntityReference(department.Id, DepartmentName), dataSource.Entity);
        var form = Assert.Single(result.Model.Forms);
        Assert.Equal(
            (ModuleResourceIds.Derive(applicationId, Guid.Parse(FormId)), "departments/DepartmentLayout", "modules/departments/forms/DepartmentLayout.json"),
            (form.Id, form.Name, form.File));
        Assert.Equal(new EntityReference(department.Id, DepartmentName), form.Entity);
    }

    [Fact]
    public void A_table_widget_can_name_a_module_data_source()
    {
        using var modules = ModulesWithTexts().With("departments/1.0.0/data-sources/Departments.json", ModuleDataSource);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("texts/en.json", EnglishTexts)
            .With("pages/departments.json", DepartmentsPage);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages).Widgets);
        Assert.Equal(
            new DataSourceReference(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(DataSourceId)), DepartmentsDataSourceName),
            widget.DataSource);
    }

    [Fact]
    public void A_module_page_over_a_module_data_source_joins_the_model_and_a_site_lists_it()
    {
        using var modules = ModulesWithPage("Departments");
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("texts/en.json", SiteTexts)
            .With("identity-providers/accounts.json", PresentationCompilerTests.AccountsProvider)
            .With("sites/main.json", MainSite);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var applicationId = Guid.Parse(ApplicationId);
        var page = Assert.Single(result.Model!.Pages);
        Assert.Equal(
            (ModuleResourceIds.Derive(applicationId, Guid.Parse(ModulePageId)), "departments/Departments", "modules/departments/pages/Departments.json", ModulePageTitleKey),
            (page.Id, page.Name, page.File, page.Title.TextKey));
        Assert.Equal(
            new DataSourceReference(ModuleResourceIds.Derive(applicationId, Guid.Parse(DataSourceId)), DepartmentsDataSourceName),
            Assert.Single(page.Widgets).DataSource);
        var entry = Assert.Single(Assert.Single(result.Model.Sites).Navigation);
        Assert.Equal(new PageReference(page.Id, "departments/Departments"), entry.Page);
    }

    [Fact]
    public void A_module_page_widget_that_names_an_application_data_source_is_reported_in_the_module_file()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/pages/Departments.json", ModulePage("Departments"))
            .With(
                "departments/1.0.0/TextsEn.json",
                Texts(ModuleTextsEnId, "en", $$"""{ "{{ModulePageTitleKey}}": "Departments" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("texts/en.json", SiteTexts)
            .With("data-sources/departments.json", DataSource(DepartmentName));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetDataSource, "modules/departments/pages/Departments.json", "/widgets/0/dataSource", Guid.Parse(ModulePageId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_page_widget_that_names_another_module_with_a_slash_is_reported_once()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/pages/Departments.json", ModulePage("other/Thing"))
            .With(
                "departments/1.0.0/TextsEn.json",
                Texts(ModuleTextsEnId, "en", $$"""{ "{{ModulePageTitleKey}}": "Departments" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""").With("texts/en.json", SiteTexts);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, "modules/departments/pages/Departments.json", "/widgets/0/dataSource", Guid.Parse(ModulePageId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_navigation_entry_that_names_a_page_of_an_undeclared_module_is_reported_at_the_page()
    {
        using var folder = Folder("[]")
            .With("texts/en.json", SiteTexts)
            .With("identity-providers/accounts.json", PresentationCompilerTests.AccountsProvider)
            .With("sites/main.json", MainSite);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, "sites/main.json", "/navigation/0/page"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_policy_grant_can_name_a_module_data_source()
    {
        using var modules = ModulesWithTexts().With("departments/1.0.0/data-sources/Departments.json", ModuleDataSource);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("texts/en.json", EnglishTexts)
            .With("roles/employee.json", Role)
            .With("policies/departments.json", DepartmentsPolicy);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var grant = Assert.Single(Assert.Single(result.Model!.Policies).Grants);
        Assert.Equal(
            new DataSourceReference(ModuleResourceIds.Derive(Guid.Parse(ApplicationId), Guid.Parse(DataSourceId)), DepartmentsDataSourceName),
            grant.DataSource);
    }

    [Fact]
    public void A_module_data_source_whose_entity_names_another_module_with_a_slash_is_reported_once()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/data-sources/Departments.json",
            ModuleDataSource.Replace("\"entity\": \"Department\"", "\"entity\": \"people/Person\"", StringComparison.Ordinal));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, "modules/departments/data-sources/Departments.json", "/entity", Guid.Parse(DataSourceId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_form_whose_entity_names_another_module_with_a_slash_is_reported_once()
    {
        using var modules = ModulesWithTexts().With(
            "departments/1.0.0/forms/DepartmentLayout.json",
            ModuleForm.Replace("\"entity\": \"Department\"", "\"entity\": \"people/Person\"", StringComparison.Ordinal));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""").With("texts/en.json", EnglishTexts);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, "modules/departments/forms/DepartmentLayout.json", "/entity", Guid.Parse(FormId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_data_source_parameter_target_with_a_slash_is_reported_once()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/data-sources/Departments.json",
            $$"""
            { "id": "{{DataSourceId}}", "kind": "dataSource", "name": "Departments", "formatVersion": 1,
              "entity": "Department", "fields": [ { "name": "name", "path": "name" } ],
              "parameters": [ { "name": "other", "type": "reference", "target": "people/Person" } ] }
            """);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.QualifiedModuleReference, "modules/departments/data-sources/Departments.json", "/parameters/0/target"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_module_data_source_parameter_can_target_an_entity_of_its_own_module()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/Department.json", NamedDepartment)
            .With(
                "departments/1.0.0/data-sources/Departments.json",
                $$"""
                { "id": "{{DataSourceId}}", "kind": "dataSource", "name": "Departments", "formatVersion": 1,
                  "entity": "Department", "fields": [ { "name": "name", "path": "name" } ],
                  "parameters": [ { "name": "other", "type": "reference", "target": "Department" } ] }
                """);
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var parameter = Assert.Single(Assert.Single(result.Model!.DataSources).Parameters);
        Assert.Equal(DepartmentName, parameter.Target!.Name);
    }

    [Fact]
    public void A_widget_and_a_grant_naming_a_data_source_of_an_undeclared_module_are_reported_at_the_data_source()
    {
        using var folder = Folder("[]")
            .With("texts/en.json", EnglishTexts)
            .With("roles/employee.json", Role)
            .With("pages/departments.json", DepartmentsPage.Replace("departments/", "other/", StringComparison.Ordinal))
            .With("policies/departments.json", DepartmentsPolicy.Replace("departments/", "other/", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "pages/departments.json", "/widgets/0/dataSource"),
                (ExpressionDiagnosticCodes.UndeclaredModuleReference, "policies/departments.json", "/grants/0/dataSource"),
            ],
            result.Diagnostics
                .Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path))
                .OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_data_source_filter_cannot_call_an_application_rule()
    {
        using var modules = Modules("1.0.0").With(
            "departments/1.0.0/data-sources/Departments.json",
            ModuleDataSource.Replace("\"fields\"", "\"filter\": \"IsKept()\", \"fields\"", StringComparison.Ordinal));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""").With(
            "rules/is-kept.json",
            """
            { "id": "77777777-7777-4777-8777-777777777701", "kind": "rule", "name": "IsKept", "formatVersion": 1,
              "parameters": [], "resultType": "boolean", "expression": "true" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            ("modules/departments/data-sources/Departments.json", "/filter", Guid.Parse(DataSourceId)),
            (diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("IsKept", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    private const string ModulePageId = "66666666-6666-4666-8666-666666666602";
    private const string ModulePageTitleKey = "departments.pages.departments.title";

    private static string ModulePage(string dataSource) => $$"""
        { "id": "{{ModulePageId}}", "kind": "page", "name": "Departments", "formatVersion": 1,
          "title": { "textKey": "{{ModulePageTitleKey}}" },
          "widgets": [ { "type": "table", "dataSource": "{{dataSource}}" } ] }
        """;

    private const string MainSite = """
        { "id": "77777777-7777-4777-8777-777777777702", "kind": "site", "name": "Main", "formatVersion": 1, "path": "main",
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "departments/Departments", "label": { "textKey": "nav.departments" } } ] }
        """;

    private const string SiteTexts = """
        { "id": "55555555-5555-4555-8555-555555555556", "kind": "text", "name": "SiteTextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Main", "nav.departments": "Departments" } }
        """;

    private static TemporaryFolder ModulesWithPage(string dataSource) =>
        Modules("1.0.0")
            .With("departments/1.0.0/data-sources/Departments.json", ModuleDataSource)
            .With("departments/1.0.0/pages/Departments.json", ModulePage(dataSource))
            .With(
                "departments/1.0.0/TextsEn.json",
                Texts(ModuleTextsEnId, "en", $$"""{ "{{ModulePageTitleKey}}": "Departments" }"""));

    private static TemporaryFolder ModulesWithTexts() =>
        Modules("1.0.0").With(
            "departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""));

    private const string ModuleRuleId = "4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e01";

    private static string ModuleRule(string id, string name, string expression) => $$"""
        { "id": "{{id}}", "kind": "rule", "name": "{{name}}", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "{{expression}}" }
        """;

    [Fact]
    public void A_module_rule_can_call_another_rule_of_its_module()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/A.json", ModuleRule(ModuleRuleId, "A", "B(value)"))
            .With("departments/1.0.0/B.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e02", "B", "value > 0"));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_module_rule_that_calls_an_application_rule_is_reported_in_the_module_file()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/IsActive.json", ModuleRule(ModuleRuleId, "IsActive", "IsUrgent(value)"));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("IsUrgent.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e03", "IsUrgent", "value > 0"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownFunction, "modules/departments/IsActive.json", "/expression", (Guid?)Guid.Parse(ModuleRuleId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Two_module_rules_that_call_each_other_are_reported_once_in_the_module()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/A.json", ModuleRule(ModuleRuleId, "A", "B(value)"))
            .With("departments/1.0.0/B.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e02", "B", "A(value)"));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.RuleCallCycle, diagnostic.Code);
        Assert.StartsWith("modules/departments/", diagnostic.File, StringComparison.Ordinal);
        Assert.Equal("/expression", diagnostic.Path);
        Assert.Null(result.Model);
    }

    [Fact]
    public void The_module_rule_check_returns_each_rule_by_module_id_and_name()
    {
        var rule = JsonSerializer.Deserialize<RuleResource>(
            ModuleRule(ModuleRuleId, "IsActive", "value > 0"), ApplicationLoader.SerializerOptions)! with
        {
            File = "modules/departments/IsActive.json",
        };
        var diagnostics = new List<Diagnostic>();

        var rules = RuleChecker.CheckModules([("departments", [rule])], new Dictionary<string, IReadOnlySet<string>>(), diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["departments/IsActive"], rules.Keys);
        Assert.NotNull(rules["departments/IsActive"].Body);
    }

    /// <summary>
    /// The module <c>departments</c> with the rule <c>IsPositive</c> and a <c>Department</c> entity with an integer
    /// <c>value</c> and one validation, in an application that declares it, plus the given application files.
    /// </summary>
    private static (TemporaryFolder Modules, TemporaryFolder Folder) ModuleValidationApplication(
        string expression, string ruleExpression = "value > 0", params (string Path, string Content)[] files)
    {
        var modules = Modules("1.0.0")
            .With("departments/1.0.0/IsPositive.json", ModuleRule(ModuleRuleId, "IsPositive", ruleExpression))
            .With("departments/1.0.0/Department.json", $$"""
                { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "value", "type": "integer" } ],
                  "validations": [ { "name": "check1", "expression": "{{expression}}", "message": { "textKey": "departments.department.invalid" }, "field": "value" } ] }
                """)
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department.invalid": "Not valid." }"""));
        var folder = Folder(DepartmentsModule).With("texts/en.json", RequestTexts);
        foreach (var (path, content) in files)
        {
            folder.With(path, content);
        }

        return (modules, folder);
    }

    private static object? EvaluateDepartment(Model.ValidationModel validation, long value)
    {
        var result = ExpressionInterpreter.Evaluate(
            validation.Syntax, validation.Check, new ExpressionValues([KeyValuePair.Create("value", (object?)value)]));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public void A_module_validation_calls_a_rule_of_its_module_by_plain_name()
    {
        var (modules, folder) = ModuleValidationApplication("IsPositive(value)");
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(result.References.Edges, edge => edge.Relation == ReferenceRelations.RuleCall);
        Assert.True(result.Model!.TryGetEntity(DepartmentName, out var department));
        var validation = Assert.Single(department.Validations);
        Assert.Equal(true, EvaluateDepartment(validation, 1L));
        Assert.Equal(false, EvaluateDepartment(validation, 0L));
    }

    [Fact]
    public void A_module_validation_that_calls_an_application_rule_is_reported_in_the_module_file()
    {
        var (modules, folder) = ModuleValidationApplication(
            "IsUrgent(value)", files: ("IsUrgent.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e03", "IsUrgent", "value > 0")));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownFunction, "modules/departments/Department.json", "/validations/0/expression", (Guid?)Guid.Parse(DepartmentId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_qualified_call_from_a_module_validation_is_an_unknown_rule()
    {
        var (modules, folder) = ModuleValidationApplication("departments:IsPositive(value)");
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownFunction, "modules/departments/Department.json", "/validations/0/expression", (Guid?)Guid.Parse(DepartmentId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_validation_calls_its_module_rule_when_an_application_rule_has_the_same_name()
    {
        var (modules, folder) = ModuleValidationApplication(
            "IsPositive(value)", files: ("IsPositive.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e04", "IsPositive", "value > 100")));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Model!.TryGetEntity(DepartmentName, out var department));
        Assert.Equal(true, EvaluateDepartment(Assert.Single(department.Validations), 1L));
    }

    private const string RequestTexts = """
        { "id": "55555555-5555-4555-8555-555555555599", "kind": "text", "name": "RequestTextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "request.invalid": "The request is not valid.", "request.cannotStart": "This request cannot be started.", "role.employee": "Employee" } }
        """;

    /// <summary>
    /// An application over the module <c>departments</c> with the rules <c>IsPositive</c> and <c>A</c>, which calls <c>B</c>,
    /// and a <c>Request</c> entity with an integer <c>quantity</c> and the given validations, plus the given extra files.
    /// </summary>
    private static (TemporaryFolder Modules, TemporaryFolder Folder) RuleApplication(
        string declaredModules, string validations = "[]", params (string Path, string Content)[] files)
    {
        var modules = Modules("1.0.0")
            .With("departments/1.0.0/IsPositive.json", ModuleRule(ModuleRuleId, "IsPositive", "value > 0"))
            .With("departments/1.0.0/A.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e02", "A", "B(value)"))
            .With("departments/1.0.0/B.json", ModuleRule("4d6e8f10-2a3b-4c5d-9e7f-1a2b3c4d5e03", "B", "value > 0 and value < 100"))
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.name": "Name" }"""));
        var folder = Folder(declaredModules)
            .With("texts/en.json", RequestTexts)
            .With("entities/request.json", $$"""
                { "id": "a1b2c3d4-0000-4000-8000-000000000002", "kind": "entity", "name": "Request", "formatVersion": 1,
                  "fields": [ { "name": "quantity", "type": "integer" } ], "validations": {{validations}} }
                """);
        foreach (var (path, content) in files)
        {
            folder.With(path, content);
        }

        return (modules, folder);
    }

    private const string DepartmentsModule = """[{ "id": "departments", "version": "1.0.0" }]""";

    private static string RequestValidation(string expression) =>
        $$"""[{ "name": "check2", "expression": "{{expression}}", "message": { "textKey": "request.invalid" }, "field": "quantity" }]""";

    private static object? EvaluateRequest(Model.ValidationModel validation, long quantity)
    {
        var result = ExpressionInterpreter.Evaluate(
            validation.Syntax, validation.Check, new ExpressionValues([KeyValuePair.Create("quantity", (object?)quantity)]));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public void An_application_validation_calls_a_module_rule_by_its_qualified_name()
    {
        var (modules, folder) = RuleApplication(DepartmentsModule, RequestValidation("departments:IsPositive(quantity)"));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Model!.TryGetEntity("Request", out var request));
        var validation = Assert.Single(request.Validations);
        Assert.Equal(true, EvaluateRequest(validation, 1L));
        Assert.Equal(false, EvaluateRequest(validation, 0L));
    }

    [Fact]
    public void A_module_rule_call_adds_no_rule_call_edge()
    {
        var (modules, folder) = RuleApplication(DepartmentsModule, RequestValidation("departments:IsPositive(quantity)"));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(result.References.Edges, edge => edge.Relation == ReferenceRelations.RuleCall);
    }

    [Fact]
    public void A_data_source_filter_inlines_a_module_rule_and_the_module_rule_it_calls()
    {
        var (modules, folder) = RuleApplication(DepartmentsModule, "[]", ("data-sources/requests.json", """
            { "id": "99999999-9999-4999-8999-999999999902", "kind": "dataSource", "name": "Requests", "formatVersion": 1, "entity": "Request",
              "fields": [{ "name": "quantity", "path": "quantity" }], "filter": "departments:A(quantity)" }
            """));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var filter = Assert.Single(result.Model!.DataSources).Filter!;
        var sql = SqlTranslator.Translate(filter.Syntax, path => string.Join('.', path), filter.Check);
        Assert.True(sql.Succeeded, sql.Diagnostic?.Message);
        Assert.Contains("quantity > @f0", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("quantity < @f1", sql.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("departments", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_qualified_call_to_an_undeclared_module_is_reported_at_the_validation_expression()
    {
        var (modules, folder) = RuleApplication(DepartmentsModule, RequestValidation("payroll:IsPositive(quantity)"));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, "entities/request.json", "/validations/0/expression"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'payroll'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_qualified_call_to_a_rule_the_declared_module_lacks_is_an_unknown_rule()
    {
        var (modules, folder) = RuleApplication(DepartmentsModule, RequestValidation("departments:Missing(quantity)"));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownFunction, "entities/request.json", "/validations/0/expression"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_process_start_condition_and_a_policy_filter_call_a_module_rule()
    {
        var (modules, folder) = RuleApplication(DepartmentsModule, "[]",
            ("roles/employee.json", Role),
            ("processes/review.json", """
                { "id": "77777777-7777-4777-8777-777777777702", "kind": "process", "name": "Review", "formatVersion": 1, "entity": "Request",
                  "startCondition": { "expression": "departments:IsPositive(quantity)", "message": { "textKey": "request.cannotStart" } },
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """),
            ("policies/requests.json", """
                { "id": "12121212-1212-4212-8212-121212121202", "kind": "policy", "name": "RequestAccess", "formatVersion": 1,
                  "roles": ["employee"],
                  "grants": [{ "entity": "Request", "actions": ["read"], "filter": "departments:A(quantity)" }] }
                """));
        using var _ = modules;
        using var __ = folder;

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        var filter = Assert.Single(Assert.Single(result.Model!.Policies).Grants).Filter!;
        var sql = SqlTranslator.Translate(filter.Syntax, path => string.Join('.', path), filter.Check);
        Assert.True(sql.Succeeded, sql.Diagnostic?.Message);
        Assert.Contains("quantity > @f0", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("quantity < @f1", sql.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("departments", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void An_application_rule_chain_through_module_rules_counts_toward_the_call_depth_limit()
    {
        // R1 .. R7 are application rules, R7 calls departments:A, and A calls B: nine rules deep.
        var (modules, folder) = RuleApplication(DepartmentsModule, RequestValidation("R1(quantity)"));
        using var _ = modules;
        using var __ = folder;
        for (var k = 1; k <= 7; k++)
        {
            var expression = k < 7 ? $"R{k + 1}(value)" : "departments:A(value)";
            folder.With($"rules/r{k}.json", ModuleRule($"66666666-6666-4666-8666-66666666670{k}", $"R{k}", expression));
        }

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.RuleCallTooDeep, "rules/r1.json", "/expression"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'departments:A'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_application_text_key_with_a_declared_module_prefix_is_reported_in_the_application_text()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("TextsEn.json", Texts(ApplicationTextsEnId, "en", """{ "departments.name": "Name" }"""));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ApplicationTextKeyWithModulePrefix, "TextsEn.json", "/texts/departments.name", Guid.Parse(ApplicationTextsEnId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_entity_label_resolves_through_the_module_text_of_the_application_locale()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/Department.json", LabelledDepartment)
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""))
            .With("departments/1.0.0/TextsVi.json", Texts(ModuleTextsViId, "vi", """{ "departments.department": "Phòng ban" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""")
            .With("TextsEn.json", Texts(ApplicationTextsEnId, "en", """{ "sample.title": "Sample" }"""));
        var installed = ModuleCatalog.Load(modules.Path).Modules;

        var compiled = ApplicationCompiler.Compile(folder.Path, null, installed);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        var text = Assert.Single(compiled.Model!.Texts);
        Assert.Equal(("en", "Department", "Sample"), (text.Locale, text.Texts["departments.department"], text.Texts["sample.title"]));
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal("Department", Assert.Single(rebuilt.Model!.Texts).Texts["departments.department"]);
    }

    [Fact]
    public void Two_module_text_files_for_one_locale_are_reported_at_the_later_file()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""))
            .With("departments/1.0.0/TextsEn2.json", Texts(ModuleTextsViId, "EN", """{ "departments.department": "Department" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateLocale, "modules/departments/TextsEn2.json", "/locale"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_text_key_missing_from_another_module_locale_is_reported_in_that_module_file()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""))
            .With("departments/1.0.0/TextsVi.json", Texts(ModuleTextsViId, "vi", "{}"));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.LocaleDrift, "modules/departments/TextsVi.json", "/texts"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_module_label_is_missing_when_the_application_has_no_text_resource()
    {
        using var modules = Modules("1.0.0")
            .With("departments/1.0.0/Department.json", LabelledDepartment)
            .With("departments/1.0.0/TextsEn.json", Texts(ModuleTextsEnId, "en", """{ "departments.department": "Department" }"""));
        using var folder = Folder("""[{ "id": "departments", "version": "1.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "modules/departments/Department.json", "/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    private const string EnglishTexts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "department.main": "Department", "role.employee": "Employee", "pages.departments.title": "Departments" } }
        """;

    private const string Role = """
        { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
        """;

    private static string DataSource(string entity) => $$"""
        { "id": "99999999-9999-4999-8999-999999999901", "kind": "dataSource", "name": "Departments", "formatVersion": 1,
          "entity": "{{entity}}", "fields": [ { "name": "name", "path": "name" } ] }
        """;

    private static string FormOver(string entity) => $$"""
        { "id": "88888888-8888-4888-8888-888888888801", "kind": "form", "name": "DepartmentLayout", "formatVersion": 1, "entity": "{{entity}}",
          "sections": [ { "title": { "textKey": "department.main" }, "fields": [ { "field": "name" } ] } ] }
        """;

    private static string PolicyOver(string entity) => $$"""
        { "id": "12121212-1212-4212-8212-121212121201", "kind": "policy", "name": "DepartmentAccess", "formatVersion": 1,
          "roles": ["employee"], "grants": [ { "entity": "{{entity}}", "actions": ["read"] } ] }
        """;

    /// <summary>An installation root with each version of the <c>departments</c> module, holding its manifest and one entity.</summary>
    private static TemporaryFolder Modules(params string[] versions)
    {
        var root = new TemporaryFolder();
        foreach (var version in versions)
        {
            root.With($"departments/{version}/module.json", ModuleCatalogTests.Manifest("departments", version));
            root.With($"departments/{version}/Department.json", Department);
        }

        return root;
    }

    private static TemporaryFolder Folder(string modules) =>
        new TemporaryFolder().With("application.json", $$"""
            { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1, "modules": {{modules}} }
            """);

    private static string Texts(string id, string locale, string texts) => $$"""
        { "id": "{{id}}", "kind": "text", "name": "Texts{{locale}}", "formatVersion": 1, "locale": "{{locale}}", "texts": {{texts}} }
        """;
}
