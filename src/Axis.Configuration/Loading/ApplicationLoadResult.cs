using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>
/// The outcome of loading an application folder. Typed resources are present for every file that
/// passed schema validation, even when other files failed. <see cref="Resources"/> holds the
/// canonical content of those same files, in path order.
/// </summary>
/// <remarks>
/// When <see cref="HasErrors"/> is true, <see cref="Application"/> and <see cref="Entities"/> hold
/// only the files that passed schema validation. They may be used to find further diagnostics, but
/// never to build a model or a release. When the folder could not be listed, the result has no
/// application, entities or resources and a single <see cref="DiagnosticCodes.UnlistableFolder"/>
/// diagnostic.
/// </remarks>
/// <param name="UnloadedEntityNames">
/// The <c>name</c> of every <c>entity</c> file that was not loaded because of its own errors, such
/// as a schema violation, compared ignoring letter case. A reference to one of these names is not
/// reported again; the file's own diagnostics already are.
/// </param>
public sealed record ApplicationLoadResult(
    ApplicationManifest? Application,
    IReadOnlyList<EntityResource> Entities,
    IReadOnlyList<ResourceContent> Resources,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlySet<string> UnloadedEntityNames)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
