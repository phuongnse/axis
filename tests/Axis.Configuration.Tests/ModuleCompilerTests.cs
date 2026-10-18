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
}
