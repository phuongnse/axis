using System.Reflection;
using System.Runtime.Loader;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Extensions;

namespace Axis.Configuration.Tests;

public sealed class PackageCatalogTests
{
    private const string ManifestFile = "test-package/1.0.0/axis-package.json";

    [Fact]
    public void An_installed_package_loads_with_its_manifest()
    {
        using var root = new TemporaryFolder().Install("1.0.0");

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Diagnostics);
        var package = Assert.Single(catalog.Packages);
        Assert.Same(package, catalog.Find(TestPackages.Id, "1.0.0"));
        Assert.Null(catalog.Find(TestPackages.Id, "2.0.0"));
        Assert.Equal(("test-package", "1.0.0", "Axis.TestPackage.dll"), (package.Manifest.Id, package.Manifest.Version, package.Manifest.Assembly));
        Assert.Equal("Test package", package.Manifest.Label);
        Assert.Equal(["describe", "sum", "fail", "flaky", "slow", "probe"], package.Manifest.Operations.Select(operation => operation.Name));
        Assert.Equal(PackageLanguage.CSharp, package.Manifest.Language);
        Assert.Equal("packages.lock.json", package.Manifest.Lock);
        Assert.Equal("test-package/1.0.0", package.LoadContext!.Name);
    }

    [Fact]
    public void Node_and_python_packages_load_from_their_manifest_without_an_assembly()
    {
        using var root = new TemporaryFolder()
            .With("node-tools/1.0.0/axis-package.json", Manifest("node-tools", """ "language": "node", "adapter": "index.js", "lock": "package-lock.json" """))
            .With("node-tools/1.0.0/index.js", "")
            .With("python-tools/1.0.0/axis-package.json", Manifest("python-tools", """ "language": "python", "adapter": "adapter.py", "lock": "requirements.txt" """))
            .With("python-tools/1.0.0/adapter.py", "");

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Diagnostics);
        Assert.Equal(
            [("node-tools", PackageLanguage.Node, "index.js"), ("python-tools", PackageLanguage.Python, "adapter.py")],
            catalog.Packages.Select(package => (package.Manifest.Id, package.Manifest.Language, package.Manifest.Adapter)));
        Assert.All(catalog.Packages, package =>
        {
            Assert.Null(package.Manifest.Assembly);
            Assert.Null(package.LoadContext);
        });
        Assert.Equal(["node-tools", "python-tools"], catalog.Versions.Select(version => version.Id));
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Packages[0].CreateOperation("run"));
        Assert.Contains("package runner", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_node_package_without_its_adapter_file_is_left_out()
    {
        using var root = new TemporaryFolder()
            .With("node-tools/1.0.0/axis-package.json", Manifest("node-tools", """ "language": "node", "adapter": "index.js", "lock": "package-lock.json" """));

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageAssemblyMissing, "node-tools/1.0.0/axis-package.json", "/adapter"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.DoesNotContain(root.Path, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_without_a_lock_is_refused()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest.Remove("lock"));

        AssertRefused(PackageCatalog.Load(root.Path), ManifestFile, "", "lock");
    }

    [Fact]
    public void A_csharp_manifest_with_an_adapter_is_refused()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest["adapter"] = "index.js");

        AssertRefused(PackageCatalog.Load(root.Path), ManifestFile, "/adapter", "adapter");
    }

    [Fact]
    public void A_node_manifest_without_an_adapter_is_refused()
    {
        using var root = new TemporaryFolder()
            .With("node-tools/1.0.0/axis-package.json", Manifest("node-tools", """ "language": "node", "lock": "package-lock.json" """));

        AssertRefused(PackageCatalog.Load(root.Path), "node-tools/1.0.0/axis-package.json", "", "adapter");
    }

    [Fact]
    public void A_node_manifest_with_an_assembly_is_refused()
    {
        using var root = new TemporaryFolder()
            .With("node-tools/1.0.0/axis-package.json", Manifest("node-tools", """ "language": "node", "adapter": "index.js", "assembly": "Tools.dll", "lock": "package-lock.json" """));

        AssertRefused(PackageCatalog.Load(root.Path), "node-tools/1.0.0/axis-package.json", "/assembly", "assembly");
    }

    [Fact]
    public void A_node_manifest_with_the_lock_of_another_language_is_refused()
    {
        using var root = new TemporaryFolder()
            .With("node-tools/1.0.0/axis-package.json", Manifest("node-tools", """ "language": "node", "adapter": "index.js", "lock": "packages.lock.json" """));

        AssertRefused(PackageCatalog.Load(root.Path), "node-tools/1.0.0/axis-package.json", "/lock", "lock");
    }

    [Fact]
    public void A_single_folder_is_checked_without_the_folder_name_rule()
    {
        using var root = new TemporaryFolder().Install("1.0.0");
        Directory.Move(Path.Combine(root.Path, "test-package", "1.0.0"), Path.Combine(root.Path, "elsewhere"));

        var check = PackageCatalog.CheckFolder(Path.Combine(root.Path, "elsewhere"));

        Assert.Empty(check.Diagnostics);
        Assert.Equal(("test-package", "1.0.0"), (check.Package!.Manifest.Id, check.Package.Manifest.Version));
    }

    [Fact]
    public void A_single_folder_without_a_manifest_is_refused_with_the_manifest_as_its_file()
    {
        using var root = new TemporaryFolder().With("source/readme.txt", "");

        var check = PackageCatalog.CheckFolder(Path.Combine(root.Path, "source"));

        Assert.Null(check.Package);
        var diagnostic = Assert.Single(check.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, "axis-package.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.DoesNotContain(root.Path, diagnostic.Message, StringComparison.Ordinal);
    }

    private static void AssertRefused(PackageCatalog catalog, string file, string path, string messagePart)
    {
        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, file, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
    }

    private static string Manifest(string id, string fields) =>
        $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "label": "Tools",
          {{fields}},
          "operations": [{ "name": "run", "type": "run", "idempotent": true }]
        }
        """;

    [Fact]
    public void A_manifest_that_fails_its_schema_is_a_diagnostic_and_the_package_is_not_listed()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest["operations"]![0]!.AsObject().Remove("idempotent"));

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, ManifestFile, "/operations/0"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'test-package'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("idempotent", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_without_a_label_is_a_diagnostic_and_the_package_is_not_listed()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest.Remove("label"));

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, ManifestFile), (diagnostic.Code, diagnostic.File));
        Assert.Contains("'test-package'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("label", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_label_of_101_characters_is_a_diagnostic_and_the_package_is_not_listed()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest["label"] = new string('a', 101));

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageManifestInvalid, ManifestFile, "/label"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'test-package'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_that_is_not_valid_JSON_or_is_missing_is_a_diagnostic()
    {
        using var root = new TemporaryFolder().Install("1.0.0", "2.0.0")
            .With("test-package/1.0.0/axis-package.json", "{")
            .With("other-package/1.0.0/readme.txt", "");

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Single(catalog.Packages, package => package.Manifest.Version == "2.0.0");
        Assert.Equal(
            [
                (DiagnosticCodes.PackageManifestInvalid, "other-package/1.0.0/axis-package.json", ""),
                (DiagnosticCodes.PackageManifestInvalid, ManifestFile, ""),
            ],
            catalog.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void An_operation_type_the_assembly_lacks_is_a_diagnostic_naming_the_package()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest["operations"]![0]!["type"] = "TestPackage.Missing");

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidPackageOperationType, ManifestFile, "/operations/0/type"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'test-package' 1.0.0", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("TestPackage.Missing", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Folder_names_that_differ_from_the_manifest_are_a_diagnostic()
    {
        using var root = new TemporaryFolder().Install("1.0.0");
        Directory.Move(Path.Combine(root.Path, "test-package", "1.0.0"), Path.Combine(root.Path, "test-package", "1.0.1"));
        Directory.Move(Path.Combine(root.Path, "test-package"), Path.Combine(root.Path, "other-package"));

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        Assert.Equal(
            [
                (DiagnosticCodes.PackageFolderMismatch, "other-package/1.0.1/axis-package.json", "/id"),
                (DiagnosticCodes.PackageFolderMismatch, "other-package/1.0.1/axis-package.json", "/version"),
            ],
            catalog.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void A_missing_assembly_is_a_diagnostic()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest["assembly"] = "Missing.dll");

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal((DiagnosticCodes.PackageAssemblyMissing, ManifestFile, "/assembly"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.DoesNotContain(root.Path, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_version_that_differs_from_the_manifest_is_a_diagnostic()
    {
        using var root = new TemporaryFolder().Install("1.0.0")
            .EditManifest("1.0.0", manifest => manifest["version"] = "1.0.1");
        Directory.Move(Path.Combine(root.Path, "test-package", "1.0.0"), Path.Combine(root.Path, "test-package", "1.0.1"));

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.PackageVersionMismatch, "test-package/1.0.1/axis-package.json", "/version"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'1.0.0'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_names_and_a_timeout_above_two_minutes_are_diagnostics()
    {
        using var root = new TemporaryFolder().Install("1.0.0").EditManifest("1.0.0", manifest =>
        {
            var sum = manifest["operations"]![1]!;
            sum["name"] = "DESCRIBE";
            sum["timeout"] = "PT3M";
            sum["input"]![1]!["name"] = "A";
        });

        var catalog = PackageCatalog.Load(root.Path);

        Assert.Empty(catalog.Packages);
        Assert.Equal(
            [
                (DiagnosticCodes.DuplicatePackageName, "/operations/1/input/1/name"),
                (DiagnosticCodes.DuplicatePackageName, "/operations/1/name"),
                (DiagnosticCodes.InvalidPackageTimeout, "/operations/1/timeout"),
            ],
            catalog.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void A_package_sees_the_hosts_contracts_and_no_other_Axis_assembly()
    {
        using var root = new TemporaryFolder().Install("1.0.0");

        var package = Assert.Single(PackageCatalog.Load(root.Path).Packages);
        var operation = package.CreateOperation("describe");

        var operationType = operation.GetType();
        Assert.Same(package.LoadContext, AssemblyLoadContext.GetLoadContext(operationType.Assembly));
        Assert.Same(typeof(IOperation), operationType.GetInterfaces().Single());
        Assert.Equal(["Axis.TestPackage"], package.LoadContext!.Assemblies.Select(assembly => assembly.GetName().Name));
        Assert.ThrowsAny<FileNotFoundException>(() => package.LoadContext!.LoadFromAssemblyName(new AssemblyName("Axis.Core")));
    }

    [Fact]
    public void Without_a_root_the_catalog_is_empty_and_a_missing_root_fails()
    {
        Assert.Empty(PackageCatalog.Load(null).Packages);
        Assert.Empty(PackageCatalog.Load("").Diagnostics);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PackageCatalog.Load(Path.Combine(Path.GetTempPath(), $"axis-missing-{Guid.NewGuid():N}")));
        Assert.Contains(PackageCatalog.PathSetting, exception.Message, StringComparison.Ordinal);
    }
}
