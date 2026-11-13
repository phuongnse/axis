using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;

namespace Axis.PackageHost.Tests;

// npm and the stub scanner need a Unix shell, so the tests are skipped on Windows before any Unix-only call is made.
// Every build writes and deletes folders named axis-package-build-* in the temporary folder, and these tests list them,
// so the classes that build packages run one after the other.
[UnsupportedOSPlatform("windows")]
[Collection(BuildTest.Collection)]
public sealed class NodePackageBuildCommandTests
{
    private const string NoFindings = """{ "Results": [] }""";

    [Fact]
    public async Task A_node_package_is_installed_from_its_lock_file_and_loads_without_diagnostics()
    {
        using var test = new BuildTest(NoFindings, node: true);

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Built, exitCode);
        Assert.Contains("Built node-test-package 1.0.0 in node-test-package/1.0.0/.", output);
        Assert.True(test.ScannerRan);
        var written = Path.Combine(test.Extensions.Path, BuildTest.NodeId, "1.0.0");
        Assert.True(File.Exists(Path.Combine(written, "node_modules", "axis-test-dependency", "package.json")));
        Assert.True(File.Exists(Path.Combine(written, "index.js")));
        Assert.False(Directory.Exists(Path.Combine(test.Source, "node_modules")));
        var catalog = PackageCatalog.Load(test.Extensions.Path);
        Assert.Empty(catalog.Diagnostics);
        Assert.NotNull(catalog.Find(BuildTest.NodeId, "1.0.0"));
    }

    [Fact]
    public async Task A_package_json_that_disagrees_with_the_lock_file_is_refused_before_npm_and_the_scan()
    {
        using var test = new BuildTest(NoFindings, node: true);
        var packageFile = Path.Combine(test.Source, "package.json");
        var package = JsonNode.Parse(File.ReadAllText(packageFile))!.AsObject();
        package["dependencies"]!["left-pad"] = "1.3.0";
        File.WriteAllText(packageFile, package.ToJsonString());
        var before = TemporaryBuildFolders();

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'package-lock.json' does not match package.json: it does not lock left-pad 1.3.0", output);
        Assert.Equal(before, TemporaryBuildFolders());
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_source_without_a_lock_file_is_refused_before_npm_and_the_scan()
    {
        using var test = new BuildTest(NoFindings, node: true);
        File.Delete(Path.Combine(test.Source, "package-lock.json"));

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'package-lock.json' is missing", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_lock_file_that_is_not_json_is_refused_without_a_path()
    {
        using var test = new BuildTest(NoFindings, node: true);
        File.WriteAllText(Path.Combine(test.Source, "package-lock.json"), "not json");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'package-lock.json' cannot be checked", output);
        Assert.DoesNotContain(test.Source, output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_lock_file_that_locks_a_dependency_that_package_json_dropped_is_refused()
    {
        using var test = new BuildTest(NoFindings, node: true);
        var packageFile = Path.Combine(test.Source, "package.json");
        var package = JsonNode.Parse(File.ReadAllText(packageFile))!.AsObject();
        package["dependencies"]!.AsObject().Remove("axis-test-dependency");
        File.WriteAllText(packageFile, package.ToJsonString());

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'package-lock.json' does not match package.json: it locks axis-test-dependency", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task An_install_script_of_a_dependency_does_not_run_during_the_build()
    {
        using var test = new BuildTest(NoFindings, node: true);

        var (exitCode, _) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Built, exitCode);
        var written = Path.Combine(test.Extensions.Path, BuildTest.NodeId, "1.0.0");
        var dependency = JsonNode.Parse(File.ReadAllText(Path.Combine(written, "node_modules", "axis-test-dependency", "package.json")))!;
        Assert.NotNull(dependency["scripts"]?["postinstall"]);
        Assert.Empty(Directory.GetFiles(written, "install-script-ran", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_failed_install_is_refused_with_the_npm_error_and_leaves_nothing_behind()
    {
        using var test = new BuildTest(NoFindings, node: true);
        File.Delete(Path.Combine(test.Source, "vendor", "axis-test-dependency-1.0.0.tgz"));
        // Without its integrity hash npm cannot take the tarball from its cache, so the missing file fails the install.
        var lockFile = Path.Combine(test.Source, "package-lock.json");
        var locked = JsonNode.Parse(File.ReadAllText(lockFile))!.AsObject();
        locked["packages"]!["node_modules/axis-test-dependency"]!.AsObject().Remove("integrity");
        File.WriteAllText(lockFile, locked.ToJsonString());
        var before = TemporaryBuildFolders();

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("npm ci failed", output);
        Assert.DoesNotContain("axis-package-build-", output);
        Assert.Equal(before, TemporaryBuildFolders());
        test.AssertNothingWrittenAndNoScan();
    }

    /// <summary>The staging folders of the package build now in the temporary folder.</summary>
    private static string[] TemporaryBuildFolders() =>
        [.. Directory.GetDirectories(Path.GetTempPath(), "axis-package-build-*").Order(StringComparer.Ordinal)];
}
