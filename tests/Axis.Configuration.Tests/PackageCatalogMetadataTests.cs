using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

/// <summary>The server reads packages as metadata only, so no package code runs, see <c>docs/reference/extension-packages.md#loading</c>.</summary>
public sealed class PackageCatalogMetadataTests
{
    private const string ManifestFile = "test-package/1.0.0/axis-package.json";

    private static readonly Dictionary<string, (Func<TemporaryFolder, TemporaryFolder> Break, string File, string Path)> _brokenPackages = new()
    {
        [DiagnosticCodes.PackageManifestInvalid] = (
            root => root.EditManifest("1.0.0", manifest => manifest["operations"]![0]!.AsObject().Remove("idempotent")),
            ManifestFile,
            "/operations/0"),
        [DiagnosticCodes.PackageFolderMismatch] = (
            root =>
            {
                Directory.Move(Path.Combine(root.Path, "test-package", "1.0.0"), Path.Combine(root.Path, "test-package", "1.0.1"));
                Directory.Move(Path.Combine(root.Path, "test-package"), Path.Combine(root.Path, "other-package"));
                return root;
            },
            "other-package/1.0.1/axis-package.json",
            "/id"),
        [DiagnosticCodes.PackageAssemblyMissing] = (
            root => root.EditManifest("1.0.0", manifest => manifest["assembly"] = "Missing.dll"),
            ManifestFile,
            "/assembly"),
        [DiagnosticCodes.PackageVersionMismatch] = (
            root =>
            {
                root.EditManifest("1.0.0", manifest => manifest["version"] = "1.0.1");
                Directory.Move(Path.Combine(root.Path, "test-package", "1.0.0"), Path.Combine(root.Path, "test-package", "1.0.1"));
                return root;
            },
            "test-package/1.0.1/axis-package.json",
            "/version"),
        [DiagnosticCodes.InvalidPackageOperationType] = (
            root => root.EditManifest("1.0.0", manifest => manifest["operations"]![0]!["type"] = "TestPackage.Missing"),
            ManifestFile,
            "/operations/0/type"),
        [DiagnosticCodes.DuplicatePackageName] = (
            root => root.EditManifest("1.0.0", manifest => manifest["operations"]![1]!["name"] = "DESCRIBE"),
            ManifestFile,
            "/operations/1/name"),
        [DiagnosticCodes.InvalidPackageTimeout] = (
            root => root.EditManifest("1.0.0", manifest => manifest["operations"]![1]!["timeout"] = "PT3M"),
            ManifestFile,
            "/operations/1/timeout"),
    };

    public static TheoryData<string> BrokenPackageCodes => [.. _brokenPackages.Keys];

    public static TheoryData<string> NonOperationTypes =>
    [
        "InitializerPackage.NotAnOperation",
        "InitializerPackage.AbstractOperation",
        "InitializerPackage.Initializer",
    ];

    [Fact]
    public void A_package_whose_code_writes_markers_is_listed_and_compiled_against_and_never_runs()
    {
        using var root = new TemporaryFolder().Install(TestPackages.InitializerBuild);

        var catalog = PackageCatalog.Load(root.Path, PackageLoadMode.MetadataOnly);

        Assert.Empty(catalog.Diagnostics);
        var package = Assert.Single(catalog.Packages);
        Assert.Equal(("initializer-package", "1.0.0"), (package.Manifest.Id, package.Manifest.Version));
        Assert.Equal(["touch"], package.Manifest.Operations.Select(operation => operation.Name));
        Assert.Null(package.LoadContext);
        var exception = Assert.Throws<InvalidOperationException>(() => package.CreateOperation("touch"));
        Assert.Contains("metadata only", exception.Message, StringComparison.Ordinal);

        using var application = new TemporaryFolder().With("application.json", """
            { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
              "packages": [{ "id": "initializer-package", "version": "1.0.0" }] }
            """);
        var result = ApplicationCompiler.Compile(application.Path, catalog.Versions);
        Assert.Empty(result.Diagnostics);

        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.marker", SearchOption.AllDirectories));
        Assert.DoesNotContain(
            AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Where(assembly => !assembly.IsDynamic),
            assembly => assembly.Location.StartsWith(root.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_package_loaded_to_run_writes_both_markers()
    {
        using var root = new TemporaryFolder().Install(TestPackages.InitializerBuild);

        var package = Assert.Single(PackageCatalog.Load(root.Path, PackageLoadMode.Run).Packages);
        _ = package.CreateOperation("touch");

        var folder = Path.Combine(root.Path, "initializer-package", "1.0.0");
        Assert.True(File.Exists(Path.Combine(folder, "module-initializer.marker")));
        Assert.True(File.Exists(Path.Combine(folder, "static-constructor.marker")));
    }

    [Fact]
    public void A_clean_package_reports_no_diagnostic_and_all_its_operations()
    {
        using var root = new TemporaryFolder().Install("1.0.0");

        var catalog = PackageCatalog.Load(root.Path, PackageLoadMode.MetadataOnly);

        Assert.Empty(catalog.Diagnostics);
        var package = Assert.Single(catalog.Packages);
        Assert.Equal(["describe", "sum", "fail", "flaky", "slow", "probe"], package.Manifest.Operations.Select(operation => operation.Name));
        Assert.Equal(catalog.Packages.Select(installed => (installed.Manifest.Id, installed.Manifest.Version)), catalog.Versions.Select(version => (version.Id, version.Version)));
    }

    [Theory]
    [MemberData(nameof(BrokenPackageCodes))]
    public void Each_manifest_check_reports_its_code_with_the_same_file_and_pointer_as_when_the_package_runs(string code)
    {
        var (breakPackage, file, path) = _brokenPackages[code];
        using var root = breakPackage(new TemporaryFolder().Install("1.0.0"));

        var catalog = PackageCatalog.Load(root.Path, PackageLoadMode.MetadataOnly);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics.Where(diagnostic => diagnostic.Code == code), diagnostic => diagnostic.Path == path);
        Assert.Equal(file, diagnostic.File);
        Assert.DoesNotContain(root.Path, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(
            PackageCatalog.Load(root.Path).Diagnostics.Select(expected => (expected.Code, expected.File, expected.Path)),
            catalog.Diagnostics.Select(actual => (actual.Code, actual.File, actual.Path)));
    }

    [Theory]
    [MemberData(nameof(NonOperationTypes))]
    public void An_operation_type_that_cannot_run_as_an_operation_reports_the_same_code_file_and_pointer_as_when_the_package_runs(string type)
    {
        using var root = new TemporaryFolder().Install(TestPackages.InitializerBuild);
        var manifestPath = Path.Combine(root.Path, "initializer-package", "1.0.0", "axis-package.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["operations"]![0]!["type"] = type;
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var catalog = PackageCatalog.Load(root.Path, PackageLoadMode.MetadataOnly);

        Assert.Empty(catalog.Packages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(DiagnosticCodes.InvalidPackageOperationType, diagnostic.Code);
        Assert.Equal("initializer-package/1.0.0/axis-package.json", diagnostic.File);
        Assert.Equal("/operations/0/type", diagnostic.Path);
        Assert.Equal(
            PackageCatalog.Load(root.Path).Diagnostics.Select(expected => (expected.Code, expected.File, expected.Path)),
            catalog.Diagnostics.Select(actual => (actual.Code, actual.File, actual.Path)));
    }
}
