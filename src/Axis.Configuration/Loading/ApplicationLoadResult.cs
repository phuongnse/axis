using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>
/// The outcome of loading an application folder. Typed resources are present for every file that
/// passed schema validation, even when other files failed.
/// </summary>
public sealed record ApplicationLoadResult(
    ApplicationManifest? Application,
    IReadOnlyList<EntityResource> Entities,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
