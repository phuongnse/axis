using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleRoleSlotTests
{
    private const string ApplicationId = "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01";
    private const string ModuleFile = "modules/departments/module.json";

    private const string Slots = """
        [ { "name": "approver", "label": { "textKey": "departments.approver" } },
          { "name": "reader", "label": { "textKey": "departments.reader" } } ]
        """;

    [Fact]
    public void An_application_that_maps_every_slot_to_a_declared_role_keeps_the_map_in_the_model()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "approver": "department-manager", "reader": "employee" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);
        var rebuilt = ApplicationCompiler.Compile(compiled.Resources);

        Assert.Empty(compiled.Diagnostics);
        var expected = new Dictionary<string, string> { ["approver"] = "department-manager", ["reader"] = "employee" };
        Assert.Equal(expected, Assert.Single(compiled.Model!.Modules).Roles);
        Assert.Empty(rebuilt.Diagnostics);
        Assert.Equal(expected, Assert.Single(rebuilt.Model!.Modules).Roles);
    }

    [Fact]
    public void A_module_without_slots_has_an_empty_map()
    {
        using var modules = Modules(null);
        using var folder = Application(null);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Empty(Assert.Single(compiled.Model!.Modules).Roles);
    }

    [Fact]
    public void An_unmapped_slot_is_reported_at_the_roles_of_the_entry()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "approver": "department-manager" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnmappedModuleRoleSlot, "application.json", "/modules/0/roles"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'reader'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Fact]
    public void An_unmapped_slot_of_an_entry_without_roles_is_reported_at_the_entry()
    {
        using var modules = Modules(Slots);
        using var folder = Application(null);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (DiagnosticCodes.UnmappedModuleRoleSlot, "application.json", "/modules/0"),
                (DiagnosticCodes.UnmappedModuleRoleSlot, "application.json", "/modules/0"),
            ],
            compiled.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_slot_mapped_to_an_undeclared_role_is_reported_at_its_key()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "approver": "department-manager", "reader": "auditor" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModuleSlotRole, "application.json", "/modules/0/roles/reader"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'auditor'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_name_is_matched_exactly()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "approver": "Department-Manager", "reader": "employee" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Contains(
            compiled.Diagnostics,
            diagnostic => (diagnostic.Code, diagnostic.Path) == (DiagnosticCodes.UnknownModuleSlotRole, "/modules/0/roles/approver"));
    }

    [Fact]
    public void A_key_that_is_no_slot_is_reported_at_the_key_with_the_pointer_escaped()
    {
        using var modules = Modules(Slots);
        using var folder = Application("""{ "approver": "department-manager", "reader": "employee", "a/b~c": "employee", "Approver": "employee" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownModuleRoleSlot, "application.json", "/modules/0/roles/Approver"),
                (DiagnosticCodes.UnknownModuleRoleSlot, "application.json", "/modules/0/roles/a~1b~0c"),
            ],
            compiled.Diagnostics
                .Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path))
                .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal));
    }

    [Fact]
    public void A_key_of_a_module_without_slots_is_reported_as_no_slot()
    {
        using var modules = Modules(null);
        using var folder = Application("""{ "approver": "employee" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownModuleRoleSlot, "application.json", "/modules/0/roles/approver"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("no role slots", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_slot_label_without_the_module_prefix_is_reported_in_the_manifest()
    {
        using var modules = Modules("""
            [ { "name": "approver", "label": { "textKey": "departments.approver" } },
              { "name": "reader", "label": { "textKey": "reader" } } ]
            """);
        using var folder = Application("""{ "approver": "department-manager", "reader": "employee" }""");

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ModuleTextKeyWithoutPrefix, ModuleFile, "/roles/1/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private static TemporaryFolder Modules(string? slots)
    {
        var roles = slots is null ? "" : $$""", "roles": {{slots}}""";
        return new TemporaryFolder()
            .With("departments/1.0.0/module.json", $$"""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments"{{roles}} }""")
            .With("departments/1.0.0/TextsEn.json", """
                { "id": "c8e2f4a6-3b5d-4f70-9c0b-1d2e3f4a5b6c", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "departments.approver": "Approver", "departments.reader": "Reader" } }
                """);
    }

    private static TemporaryFolder Application(string? mapping)
    {
        var roles = mapping is null ? "" : $$""", "roles": {{mapping}}""";
        return new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "{{ApplicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1,
                  "modules": [{ "id": "departments", "version": "1.0.0"{{roles}} }] }
                """)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "role.employee": "Employee", "role.manager": "Manager" } }
                """)
            .With("roles/employee.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
                """)
            .With("roles/manager.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "department-manager", "formatVersion": 1, "label": { "textKey": "role.manager" } }
                """);
    }
}
