using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// Scans the dependencies of a package source with Trivy and refuses a vulnerability of high or critical
/// severity, see docs/reference/extension-packages.md#building-a-package. The setting
/// <c>PackageBuild:Trivy</c> names the Trivy command. The scan fails closed: a scan that cannot run, exits
/// with an error or prints no valid report is a setup error, never a pass.
/// </summary>
internal static class TrivyScanner
{
    public const string CommandSetting = "PackageBuild:Trivy";

    private const string DefaultCommand = "trivy";

    /// <summary>The outcome of a scan: the findings that refuse the build, or the reason the scan did not complete.</summary>
    public sealed record ScanResult(IReadOnlyList<string> Findings, string? Failure);

    public static async Task<ScanResult> ScanAsync(IConfiguration configuration, string source, CancellationToken cancellationToken)
    {
        var command = configuration[CommandSetting] is { Length: > 0 } configured ? configured : DefaultCommand;
        var result = await ExternalCommand.RunAsync(
            command,
            ["fs", "--scanners", "vuln", "--format", "json", "--quiet", "--exit-code", "0", source],
            workingDirectory: null,
            cancellationToken);
        if (result is null)
        {
            return new ScanResult([], $"The scan could not start. Set '{CommandSetting}' to the Trivy command.");
        }

        if (result.ExitCode != 0)
        {
            return new ScanResult([], $"The scan failed: Trivy exited with code {result.ExitCode}.");
        }

        try
        {
            return new ScanResult(Findings(result.Output), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new ScanResult([], "The scan failed: Trivy printed no valid JSON report.");
        }
    }

    /// <summary>One line for each distinct high or critical vulnerability of the report, as <c>Results[].Vulnerabilities[]</c>.</summary>
    private static List<string> Findings(string report)
    {
        using var document = JsonDocument.Parse(report);
        var findings = new List<string>();
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The report is not an object.");
        }

        if (!document.RootElement.TryGetProperty("Results", out var results) || results.ValueKind == JsonValueKind.Null)
        {
            return findings;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("Vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            foreach (var vulnerability in vulnerabilities.EnumerateArray())
            {
                var severity = vulnerability.GetProperty("Severity").GetString() ?? "";
                if (!severity.Equals("HIGH", StringComparison.OrdinalIgnoreCase) && !severity.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var finding = $"{vulnerability.GetProperty("PkgName").GetString()} {vulnerability.GetProperty("InstalledVersion").GetString()} has a {severity} vulnerability {vulnerability.GetProperty("VulnerabilityID").GetString()}.";
                if (!findings.Contains(finding))
                {
                    findings.Add(finding);
                }
            }
        }

        return findings;
    }
}
