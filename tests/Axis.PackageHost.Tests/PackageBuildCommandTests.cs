using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost.Tests;

// The stub scanner is a shell script, so the tests are skipped on Windows before any Unix-only call is made.
[UnsupportedOSPlatform("windows")]
public sealed class PackageBuildCommandTests
{
    private const string NoFindings = """{ "Results": [] }""";

    private const string HighFinding = """
        {
          "Results": [
            {
              "Target": "packages.lock.json",
              "Vulnerabilities": [
                { "PkgName": "Newtonsoft.Json", "InstalledVersion": "12.0.1", "VulnerabilityID": "CVE-2024-21907", "Severity": "HIGH" },
                { "PkgName": "Newtonsoft.Json", "InstalledVersion": "12.0.1", "VulnerabilityID": "CVE-2024-21907", "Severity": "HIGH" },
                { "PkgName": "Lowly.Library", "InstalledVersion": "1.0.0", "VulnerabilityID": "CVE-2024-00001", "Severity": "MEDIUM" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task A_published_package_is_built_and_loads_without_diagnostics()
    {
        using var test = new BuildTest(NoFindings);

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Built, exitCode);
        Assert.Contains("Built test-package 1.0.0 in test-package/1.0.0/.", output);
        Assert.True(test.ScannerRan);
        var catalog = PackageCatalog.Load(test.Extensions.Path);
        Assert.Empty(catalog.Diagnostics);
        Assert.NotNull(catalog.Find(TestPackages.Id, "1.0.0"));
    }

    [Fact]
    public async Task A_source_without_a_lock_file_is_refused_before_the_scan()
    {
        using var test = new BuildTest(NoFindings);
        File.Delete(Path.Combine(test.Source, "packages.lock.json"));

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("packages.lock.json", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_stale_lock_file_is_refused_before_the_scan()
    {
        using var test = new BuildTest(NoFindings);
        var depsFile = Path.Combine(test.Source, "Axis.TestPackage.deps.json");
        File.WriteAllText(depsFile, File.ReadAllText(depsFile).Replace(
            "\"libraries\": {",
            "\"libraries\": { \"Newtonsoft.Json/13.0.3\": { \"type\": \"package\" },"));

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'packages.lock.json' is stale: it does not resolve Newtonsoft.Json 13.0.3", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_lock_file_that_is_not_json_is_refused_without_a_path()
    {
        using var test = new BuildTest(NoFindings);
        File.WriteAllText(Path.Combine(test.Source, "packages.lock.json"), "not json");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'packages.lock.json' cannot be checked", output);
        Assert.DoesNotContain(test.Source, output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_high_finding_refuses_the_build_and_writes_nothing()
    {
        using var test = new BuildTest(HighFinding);

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("Newtonsoft.Json 12.0.1 has a HIGH vulnerability CVE-2024-21907.", output);
        Assert.DoesNotContain("Lowly.Library", output);
        Assert.Equal(1, output.Split("Newtonsoft.Json 12.0.1").Length - 1);
        Assert.True(test.ScannerRan);
        Assert.False(Directory.Exists(Path.Combine(test.Extensions.Path, TestPackages.Id)));
    }

    [Fact]
    public async Task A_scan_that_prints_no_report_is_a_setup_error_and_writes_nothing()
    {
        using var test = new BuildTest("not json");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.SetupError, exitCode);
        Assert.Contains("no valid JSON report", output);
        Assert.False(Directory.Exists(Path.Combine(test.Extensions.Path, TestPackages.Id)));
    }

    [Fact]
    public async Task A_scan_that_cannot_start_is_a_setup_error_and_writes_nothing()
    {
        using var test = new BuildTest(NoFindings);
        test.TrivyCommand = Path.Combine(test.Scripts.Path, "missing-trivy");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.SetupError, exitCode);
        Assert.Contains("PackageBuild:Trivy", output);
        Assert.DoesNotContain(test.Scripts.Path, output);
        Assert.False(Directory.Exists(Path.Combine(test.Extensions.Path, TestPackages.Id)));
    }

    [Fact]
    public async Task An_existing_version_folder_is_refused_and_left_as_it_was()
    {
        using var test = new BuildTest(NoFindings);
        test.Extensions.With("test-package/1.0.0/earlier.txt", "earlier");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("test-package/1.0.0", output);
        Assert.Equal(["test-package/1.0.0/earlier.txt"], Files(test.Extensions.Path));
        Assert.Equal("earlier", File.ReadAllText(Path.Combine(test.Extensions.Path, "test-package/1.0.0/earlier.txt")));
        Assert.False(test.ScannerRan);
    }

    [Fact]
    public async Task A_manifest_that_breaks_a_loading_rule_is_refused_with_its_code_and_pointer()
    {
        using var test = new BuildTest(NoFindings);
        test.EditSourceManifest(manifest => manifest["operations"]![0]!["timeout"] = "PT5M");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains($"{DiagnosticCodes.InvalidPackageTimeout} axis-package.json /operations/0/timeout:", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_source_without_a_manifest_is_refused_with_its_code()
    {
        using var test = new BuildTest(NoFindings);
        File.Delete(Path.Combine(test.Source, "axis-package.json"));

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains($"{DiagnosticCodes.PackageManifestInvalid} axis-package.json", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_node_package_is_refused_because_only_csharp_and_python_packages_are_built()
    {
        using var test = new BuildTest(NoFindings);
        File.Delete(Path.Combine(test.Source, "Axis.TestPackage.dll"));
        test.EditSourceManifest(manifest =>
        {
            manifest.Remove("assembly");
            manifest["language"] = "node";
            manifest["adapter"] = "index.js";
            manifest["lock"] = "package-lock.json";
        });
        File.WriteAllText(Path.Combine(test.Source, "index.js"), "");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("Only C# and Python packages can be built", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_python_package_is_installed_from_hashed_requirements_and_loads_without_diagnostics()
    {
        using var test = BuildTest.Python(NoFindings);

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Built, exitCode);
        Assert.Contains("Built python-test-package 1.0.0 in python-test-package/1.0.0/.", output);
        Assert.True(test.ScannerRan);
        var written = Path.Combine(test.Extensions.Path, "python-test-package", "1.0.0");
        Assert.True(File.Exists(Path.Combine(written, "adapter.py")));
        Assert.True(File.Exists(Path.Combine(written, "axis_fixture_dependency", "__init__.py")));
        Assert.True(Directory.Exists(Path.Combine(written, "axis_fixture_dependency-1.0.0.dist-info")));
        var catalog = PackageCatalog.Load(test.Extensions.Path);
        Assert.Empty(catalog.Diagnostics);
        Assert.NotNull(catalog.Find("python-test-package", "1.0.0"));
    }

    [Fact]
    public async Task A_requirement_without_a_hash_is_refused_before_the_install()
    {
        using var test = BuildTest.Python(NoFindings, _ => "axis-fixture-dependency==1.0.0\n");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'requirements.txt' line 1 has no --hash: axis-fixture-dependency==1.0.0.", output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_wheel_whose_hash_differs_is_refused_and_writes_nothing()
    {
        var other = Convert.ToHexStringLower(SHA256.HashData("other"u8));
        using var test = BuildTest.Python(NoFindings, _ => $"./wheels/{BuildTest.WheelName} --hash=sha256:{other}\n");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.Refused, exitCode);
        Assert.Contains("'requirements.txt'", output);
        Assert.DoesNotContain(test.Source, output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task A_python_command_that_cannot_start_is_a_setup_error()
    {
        using var test = BuildTest.Python(NoFindings);
        test.PythonCommand = Path.Combine(test.Scripts.Path, "missing-python");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.SetupError, exitCode);
        Assert.Contains("PackageBuild:Python", output);
        Assert.DoesNotContain(test.Scripts.Path, output);
        test.AssertNothingWrittenAndNoScan();
    }

    [Fact]
    public async Task An_extensions_folder_that_does_not_exist_is_a_setup_error()
    {
        using var test = new BuildTest(NoFindings);
        test.ExtensionsPath = Path.Combine(test.Extensions.Path, "missing");

        var (exitCode, output) = await test.RunAsync();

        Assert.Equal(PackageBuildCommand.SetupError, exitCode);
        Assert.Contains("Extensions:Path", output);
        Assert.False(test.ScannerRan);
    }

    [Fact]
    public async Task Other_arguments_print_the_usage_and_are_a_setup_error()
    {
        using var test = new BuildTest(NoFindings);

        var writer = new StringWriter();
        var exitCode = await PackageBuildCommand.RunAsync(["install", test.Source], test.Configuration(), writer, TestContext.Current.CancellationToken);

        Assert.Equal(PackageBuildCommand.SetupError, exitCode);
        Assert.Contains("Usage: Axis.PackageHost build <source>", writer.ToString());
    }

    private static string[] Files(string folder) =>
        [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    /// <summary>
    /// A copy of the test package 1.0.0 build as the source, or the Python test package with a wheel that the test
    /// builds and hashes, an empty extensions folder, and a stub in place of Trivy that leaves a marker file when it
    /// runs and prints a given report.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private sealed class BuildTest : IDisposable
    {
        public const string WheelName = "axis_fixture_dependency-1.0.0-py3-none-any.whl";

        private readonly TemporaryFolder _sources;
        private readonly string _stub;

        public BuildTest(string report)
            : this(report, new TemporaryFolder().Install("1.0.0"), Path.Combine(TestPackages.Id, "1.0.0"))
        {
        }

        private BuildTest(string report, TemporaryFolder sources, string sourcePath)
        {
            _sources = sources;
            Source = Path.Combine(_sources.Path, sourcePath);
            Assert.SkipWhen(OperatingSystem.IsWindows(), "The stub scanner is a shell script.");
            _stub = Path.Combine(Scripts.Path, "trivy");
            File.WriteAllText(_stub, $"#!/bin/sh\ntouch \"$0.ran\"\ncat <<'JSON'\n{report}\nJSON\n");
            File.SetUnixFileMode(_stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            TrivyCommand = _stub;
            ExtensionsPath = Extensions.Path;
        }

        public TemporaryFolder Extensions { get; } = new();

        public TemporaryFolder Scripts { get; } = new();

        public string Source { get; }

        public string? PythonCommand { get; set; }

        /// <summary>
        /// The Python test package, with its dependency zipped into a wheel in <c>wheels/</c>. <paramref name="requirements"/>
        /// turns the wheel's SHA-256 hash into the text of <c>requirements.txt</c>, and by default locks the wheel with it.
        /// </summary>
        public static BuildTest Python(string report, Func<string, string>? requirements = null)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "PythonPackage");
            var sources = new TemporaryFolder();
            var source = Path.Combine(sources.Path, "source");
            CopyFolder(Path.Combine(root, "package"), source);
            Directory.CreateDirectory(Path.Combine(source, "wheels"));
            var wheel = Path.Combine(source, "wheels", WheelName);
            ZipFile.CreateFromDirectory(Path.Combine(root, "dependency"), wheel);
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(wheel)));
            File.WriteAllText(
                Path.Combine(source, "requirements.txt"),
                requirements?.Invoke(hash) ?? $"./wheels/{WheelName} --hash=sha256:{hash}\n");
            return new BuildTest(report, sources, "source");
        }

        private static void CopyFolder(string from, string to)
        {
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        public string TrivyCommand { get; set; }

        public string ExtensionsPath { get; set; }

        public bool ScannerRan => File.Exists($"{_stub}.ran");

        public IConfiguration Configuration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [PackageCatalog.PathSetting] = ExtensionsPath,
                    ["PackageBuild:Trivy"] = TrivyCommand,
                    ["PackageBuild:Python"] = PythonCommand,
                })
                .Build();

        public async Task<(int ExitCode, string Output)> RunAsync()
        {
            var writer = new StringWriter();
            var exitCode = await PackageBuildCommand.RunAsync(["build", Source], Configuration(), writer, TestContext.Current.CancellationToken);
            return (exitCode, writer.ToString());
        }

        public void EditSourceManifest(Action<System.Text.Json.Nodes.JsonObject> edit)
        {
            var path = Path.Combine(Source, PackageCatalog.ManifestFileName);
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            edit(manifest);
            File.WriteAllText(path, manifest.ToJsonString());
        }

        /// <summary>A refusal before the scan writes no folder and never runs the scanner.</summary>
        public void AssertNothingWrittenAndNoScan()
        {
            Assert.Empty(Files(Extensions.Path));
            Assert.False(ScannerRan);
        }

        public void Dispose()
        {
            _sources.Dispose();
            Extensions.Dispose();
            Scripts.Dispose();
        }
    }
}
