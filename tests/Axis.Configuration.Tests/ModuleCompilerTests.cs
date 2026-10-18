using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;

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
    public void A_data_source_and_a_form_over_an_undeclared_module_are_reported_at_the_entity()
    {
        using var folder = Folder("[]")
            .With("texts/en.json", EnglishTexts)
            .With("data-sources/departments.json", DataSource("other/Department"))
            .With("forms/department.json", FormOver("other/Department"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UndeclaredModuleReference, "data-sources/departments.json", "/entity"),
                (DiagnosticCodes.UndeclaredModuleReference, "forms/department.json", "/entity"),
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
            (DiagnosticCodes.UndeclaredModuleReference, "Request.json", "/fields/0/target"),
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
        Assert.Equal(DepartmentName, process.Entity.Name);
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
    [InlineData("""{ "kind": "process" }""")]
    [InlineData("""{ "kind": "page" }""")]
    [InlineData("""{ "kind": "role" }""")]
    [InlineData("""{ "kind": "policy" }""")]
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

    /// <summary>An installation root with each version of the <c>departments</c> module, holding its manifest and one entity.</summary>
    private const string EnglishTexts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "department.main": "Department", "role.employee": "Employee" } }
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
