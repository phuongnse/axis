using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;

namespace Axis.Server.Studio;

/// <summary>
/// One problem found in a Studio folder. <c>Severity</c> is <c>error</c> or <c>warning</c>, and
/// <c>ResourceId</c> is the lower-case id of the resource the problem belongs to, or null.
/// </summary>
internal sealed record StudioDiagnostic(string Code, string Severity, string Message, string File, string Path, string? ResourceId);

/// <summary>
/// Compiles a Studio folder in memory, against the installed extension packages and modules and no database, and
/// lists the compile diagnostics in the compiler's order. The problems of a broken extension package are
/// not listed. They belong to the server's install, not to the folder.
/// </summary>
internal static class StudioDiagnostics
{
    public static IReadOnlyList<StudioDiagnostic> Compile(string folder, PackageCatalog packages, ModuleCatalog modules) =>
        [.. ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules).Diagnostics.Select(Map)];

    /// <summary>Compiles application files held in memory, as <see cref="Compile"/> compiles a folder.</summary>
    public static IReadOnlyList<StudioDiagnostic> CompileFiles(
        IReadOnlyList<ResourceContent> files, PackageCatalog packages, ModuleCatalog modules) =>
        [.. ApplicationCompiler.CompileFiles(files, packages.Versions, modules.Modules).Diagnostics.Select(Map)];

    internal static StudioDiagnostic Map(Diagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning",
        diagnostic.Message,
        diagnostic.File,
        diagnostic.Path,
        diagnostic.ResourceId?.ToString("D"));
}
