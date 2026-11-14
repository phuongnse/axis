using System.Runtime.Versioning;
using Axis.Configuration.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// A stub for Trivy, a shell script that reports what the test says and notes that it ran, for <c>PackageBuild:Trivy</c> of a
/// package host, see docs/reference/extension-packages.md#building-a-package. It is skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class StubTrivy : IDisposable
{
    /// <summary>A report with no vulnerability.</summary>
    public const string NoFindings = """{"Results":[]}""";

    /// <summary>A report with a HIGH vulnerability in <c>phonenumbers</c>.</summary>
    public const string HighFinding = """{"Results":[{"Vulnerabilities":[{"Severity":"HIGH","PkgName":"phonenumbers","InstalledVersion":"9.0.43","VulnerabilityID":"CVE-2099-0001"}]}]}""";

    private readonly TemporaryFolder _scripts = new();

    public StubTrivy(string report)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stub scanner is a shell script.");
        Command = Path.Combine(_scripts.Path, "trivy");
        File.WriteAllText(Command, $"#!/bin/sh\ntouch \"$0.ran\"\ncat <<'JSON'\n{report}\nJSON\n");
        File.SetUnixFileMode(Command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>The path of the script, the value of <c>PackageBuild:Trivy</c>.</summary>
    public string Command { get; }

    /// <summary>Whether the host ran the scanner.</summary>
    public bool Ran => File.Exists($"{Command}.ran");

    public void Dispose() => _scripts.Dispose();
}
