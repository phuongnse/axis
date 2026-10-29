using System.Runtime.Versioning;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleCatalogTests
{
    private const string ManifestFile = "departments/1.0.0/module.json";

    [Fact]
    public void An_installed_module_loads_with_its_manifest_and_files()
    {
        using var root = new TemporaryFolder()
            .With(ManifestFile, Manifest("departments", "1.0.0"))
            .With("departments/1.0.0/entities/Department.json", """{ "kind": "entity" }""")
            .With("departments/1.0.0/readme.txt", "ignored");

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Diagnostics);
        var module = Assert.Single(catalog.Modules);
        Assert.Equal(new ModuleManifest("departments", "1.0.0", "Departments"), module.Manifest);
        Assert.Equal(["entities/Department.json", "module.json"], module.Files.Select(file => file.Path));
        Assert.Equal("""{ "kind": "entity" }""", module.Files[0].Content);
    }

    [Fact]
    public void Modules_are_listed_by_id_and_then_by_version_number()
    {
        using var root = new TemporaryFolder()
            .With("departments/1.10.0/module.json", Manifest("departments", "1.10.0"))
            .With("departments/1.2.0/module.json", Manifest("departments", "1.2.0"))
            .With("assets/1.0.0/module.json", Manifest("assets", "1.0.0"));

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Equal(
            [("assets", "1.0.0"), ("departments", "1.2.0"), ("departments", "1.10.0")],
            catalog.Modules.Select(module => (module.Manifest.Id, module.Manifest.Version)));
    }

    [Fact]
    public void A_missing_manifest_is_a_diagnostic_and_the_other_versions_still_load()
    {
        using var root = new TemporaryFolder()
            .With("departments/1.0.0/readme.txt", "")
            .With("departments/1.1.0/module.json", Manifest("departments", "1.1.0"));

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Equal("1.1.0", Assert.Single(catalog.Modules).Manifest.Version);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleManifestInvalid, ManifestFile, ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_manifest_that_is_not_valid_JSON_is_a_diagnostic()
    {
        using var root = new TemporaryFolder().With(ManifestFile, "{");

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Modules);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleManifestInvalid, ManifestFile, ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("not valid JSON", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_with_a_repeated_property_is_a_diagnostic()
    {
        using var root = new TemporaryFolder().With(ManifestFile, """{ "kind": "module", "kind": "module" }""");

        var catalog = ModuleCatalog.Load(root.Path);

        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(DiagnosticCodes.ModuleManifestInvalid, diagnostic.Code);
        Assert.Contains("more than once", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0.0" }""", "")]
    [InlineData("""{ "kind": "application", "id": "departments", "version": "1.0.0", "label": "D" }""", "/kind")]
    [InlineData("""{ "kind": "module", "id": "Departments", "version": "1.0.0", "label": "D" }""", "/id")]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0", "label": "D" }""", "/version")]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "" }""", "/label")]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "D", "other": 1 }""", "/other")]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "D", "packages": [{ "id": "x" }] }""", "/packages/0")]
    [InlineData("""{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "D", "modules": [{ "id": "people", "version": "^1.0.0", "roles": {} }] }""", "/modules/0/roles")]
    public void A_manifest_that_fails_its_schema_is_a_diagnostic_at_the_failing_pointer(string manifest, string path)
    {
        using var root = new TemporaryFolder().With(ManifestFile, manifest);

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Modules);
        Assert.Contains(
            catalog.Diagnostics,
            diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path) == (DiagnosticCodes.ModuleManifestInvalid, ManifestFile, path));
    }

    [Fact]
    public void A_manifest_may_list_packages_in_the_form_of_an_application()
    {
        using var root = new TemporaryFolder().With(
            ManifestFile,
            """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "D", "packages": [{ "id": "test-package", "version": "^1.0.0" }] }""");

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Diagnostics);
        Assert.Single(catalog.Modules);
    }

    [Fact]
    public void A_manifest_id_that_differs_from_its_folder_is_reported_at_id()
    {
        using var root = new TemporaryFolder().With(ManifestFile, Manifest("people", "1.0.0"));

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Modules);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleFolderMismatch, ManifestFile, "/id"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_manifest_version_that_differs_from_its_folder_is_reported_at_version()
    {
        using var root = new TemporaryFolder().With(ManifestFile, Manifest("departments", "1.0.1"));

        var catalog = ModuleCatalog.Load(root.Path);

        Assert.Empty(catalog.Modules);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.ModuleFolderMismatch, ManifestFile, "/version"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_null_or_empty_root_gives_an_empty_catalog()
    {
        Assert.Same(ModuleCatalog.Empty, ModuleCatalog.Load(null));
        Assert.Same(ModuleCatalog.Empty, ModuleCatalog.Load(""));
        Assert.Empty(ModuleCatalog.Empty.Modules);
        Assert.Empty(ModuleCatalog.Empty.Diagnostics);
    }

    [Fact]
    public void A_missing_root_throws_and_names_the_setting()
    {
        using var root = new TemporaryFolder();

        var exception = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Load(Path.Combine(root.Path, "missing")));

        Assert.Contains(ModuleCatalog.PathSetting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_file_of_a_module_version_that_cannot_be_read_is_a_diagnostic_at_that_file()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File permissions are not Unix modes on Windows.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "A privileged process ignores file permissions.");
        using var root = new TemporaryFolder()
            .With(ManifestFile, Manifest("departments", "1.0.0"))
            .With("departments/1.0.0/Department.json", "{}");
        var file = Path.Combine(root.Path, "departments/1.0.0/Department.json");
        File.SetUnixFileMode(file, UnixFileMode.None);

        try
        {
            var catalog = ModuleCatalog.Load(root.Path);

            Assert.Empty(catalog.Modules);
            var diagnostic = Assert.Single(catalog.Diagnostics);
            Assert.Equal(
                (DiagnosticCodes.ModuleManifestInvalid, "departments/1.0.0/Department.json", ""),
                (diagnostic.Code, diagnostic.File, diagnostic.Path));
        }
        finally
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    internal static string Manifest(string id, string version) =>
        $$"""{ "kind": "module", "id": "{{id}}", "version": "{{version}}", "label": "Departments" }""";
}
