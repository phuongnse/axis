using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>
/// The outcome of loading an application folder. Typed resources are present for every file that
/// passed schema validation, even when other files failed. <see cref="Resources"/> holds the
/// canonical content of those same files, in path order.
/// </summary>
public sealed record ApplicationLoadResult(
    ApplicationManifest? Application,
    IReadOnlyList<EntityResource> Entities,
    IReadOnlyList<ResourceContent> Resources,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
