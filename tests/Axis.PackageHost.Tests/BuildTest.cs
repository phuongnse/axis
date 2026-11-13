using System.Runtime.Versioning;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost.Tests;

/// <summary>
/// A copy of the test package 1.0.0 build, or of the Node.js test package, as the source, an empty extensions
/// folder, and a stub in place of Trivy that leaves a marker file when it runs and prints a given report.
/// </summary>
// The stub scanner is a shell script, so the tests are skipped on Windows before any Unix-only call is made.
[UnsupportedOSPlatform("windows")]
internal sealed class BuildTest : IDisposable
{
    public const string NodeId = "node-test-package";

    private readonly TemporaryFolder _sources = new();
    private readonly string _stub;

    public BuildTest(string report, bool node = false)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stub scanner is a shell script.");
        if (node)
        {
            Source = Path.Combine(_sources.Path, NodeId);
            CopyFolder(Path.Combine(AppContext.BaseDirectory, "NodeTestPackage"), Source);
        }
        else
        {
            _sources.Install("1.0.0");
            Source = Path.Combine(_sources.Path, TestPackages.Id, "1.0.0");
        }

        _stub = Path.Combine(Scripts.Path, "trivy");
        File.WriteAllText(_stub, $"#!/bin/sh\ntouch \"$0.ran\"\ncat <<'JSON'\n{report}\nJSON\n");
        File.SetUnixFileMode(_stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        TrivyCommand = _stub;
        ExtensionsPath = Extensions.Path;
    }

    public TemporaryFolder Extensions { get; } = new();

    public TemporaryFolder Scripts { get; } = new();

    public string Source { get; }

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

    private static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
