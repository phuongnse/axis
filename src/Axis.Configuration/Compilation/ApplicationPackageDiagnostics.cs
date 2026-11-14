using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The diagnostics about application packages that more than one project reports, so each has one text, see
/// <c>docs/reference/extension-packages.md#an-applications-packages</c>.
/// </summary>
public static class ApplicationPackageDiagnostics
{
    /// <summary>The <c>AXC0148</c> diagnostic: application packages are off in this installation. <paramref name="file"/> is the package's manifest.</summary>
    public static Diagnostic Off(string file) => new(
        DiagnosticCodes.ApplicationPackagesOff,
        "Application packages are off in this installation (Extensions:ApplicationPackages). Remove the package or ask the operator to turn them on.",
        file,
        "");
}
