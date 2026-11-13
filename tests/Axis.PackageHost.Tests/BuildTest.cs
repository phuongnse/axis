using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost.Tests;

/// <summary>
/// A copy of the test package 1.0.0 build, of the Node.js test package, or of the Python test package with a wheel
/// that the test builds and hashes, as the source, an empty extensions folder, and a stub in place of Trivy that
/// leaves a marker file when it runs and prints a given report.
/// </summary>
// The stub scanner is a shell script, so the tests are skipped on Windows before any Unix-only call is made.
[UnsupportedOSPlatform("windows")]
internal sealed class BuildTest : IDisposable
{
    /// <summary>The xUnit collection of the classes that build packages, which run one after the other.</summary>
    public const string Collection = "Package build";

    public const string NodeId = "node-test-package";

    public const string WheelName = "axis_fixture_dependency-1.0.0-py3-none-any.whl";

    private readonly TemporaryFolder _sources = new();
    private readonly string _stub;

    public BuildTest(string report, bool node = false)
        : this(report, node ? Language.Node : Language.CSharp, null)
    {
    }

    private BuildTest(string report, Language language, Func<string, string>? requirements)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stub scanner is a shell script.");
        switch (language)
        {
            case Language.Node:
                Source = Path.Combine(_sources.Path, NodeId);
                CopyFolder(Path.Combine(AppContext.BaseDirectory, "NodeTestPackage"), Source);
                break;
            case Language.Python:
                Source = Path.Combine(_sources.Path, "source");
                CopyPython(Source, requirements);
                break;
            default:
                _sources.Install("1.0.0");
                Source = Path.Combine(_sources.Path, TestPackages.Id, "1.0.0");
                break;
        }

        _stub = Path.Combine(Scripts.Path, "trivy");
        File.WriteAllText(_stub, $"#!/bin/sh\ntouch \"$0.ran\"\ncat <<'JSON'\n{report}\nJSON\n");
        File.SetUnixFileMode(_stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        TrivyCommand = _stub;
        ExtensionsPath = Extensions.Path;
    }

    private enum Language
    {
        CSharp,
        Node,
        Python,
    }

    /// <summary>
    /// The Python test package, with its dependency zipped into a wheel in <c>wheels/</c>. <paramref name="requirements"/>
    /// turns the wheel's SHA-256 hash into the text of <c>requirements.txt</c>, and by default locks the wheel with it.
    /// </summary>
    public static BuildTest Python(string report, Func<string, string>? requirements = null) =>
        new(report, Language.Python, requirements);

    public TemporaryFolder Extensions { get; } = new();

    public TemporaryFolder Scripts { get; } = new();

    public string Source { get; }

    public string? PythonCommand { get; set; }

    public string TrivyCommand { get; set; }

    public string ExtensionsPath { get; set; }

    public bool ScannerRan => File.Exists($"{_stub}.ran");

    public static string[] Files(string folder) =>
        [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).Order(StringComparer.Ordinal)];

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

    private static void CopyPython(string source, Func<string, string>? requirements)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "PythonPackage");
        CopyFolder(Path.Combine(root, "package"), source);
        Directory.CreateDirectory(Path.Combine(source, "wheels"));
        var wheel = Path.Combine(source, "wheels", WheelName);
        ZipFile.CreateFromDirectory(Path.Combine(root, "dependency"), wheel);
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(wheel)));
        File.WriteAllText(
            Path.Combine(source, "requirements.txt"),
            requirements?.Invoke(hash) ?? $"./wheels/{WheelName} --hash=sha256:{hash}\n");
    }

    internal static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
