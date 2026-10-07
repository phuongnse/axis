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
/// When <see cref="HasErrors"/> is true, <see cref="Application"/>, <see cref="Entities"/>,
/// <see cref="Sites"/>, <see cref="Pages"/>, <see cref="Texts"/> and <see cref="Seeds"/> hold only
/// the files that passed schema validation. They may be used to find further diagnostics, but never
/// to build a model or a release. When the folder could not be listed, the result has no
/// application, entities, sites, pages, texts, seeds or resources and a single <see cref="DiagnosticCodes.UnlistableFolder"/>
/// diagnostic.
/// </remarks>
/// <param name="UnloadedEntityNames">
/// The <c>name</c> of every <c>entity</c> file that was not loaded because of its own errors, such
/// as a schema violation, compared ignoring letter case. A reference to one of these names is not
/// reported again; the file's own diagnostics already are.
/// </param>
/// <param name="UnloadedPageNames">
/// The <c>name</c> of every <c>page</c> file that was not loaded because of its own errors,
/// compared ignoring letter case. A reference to one of these names is not reported again either.
/// </param>
public sealed record ApplicationLoadResult(
    ApplicationManifest? Application,
    IReadOnlyList<EntityResource> Entities,
    IReadOnlyList<SiteResource> Sites,
    IReadOnlyList<PageResource> Pages,
    IReadOnlyList<TextResource> Texts,
    IReadOnlyList<SeedResource> Seeds,
    IReadOnlyList<ResourceContent> Resources,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlySet<string> UnloadedEntityNames,
    IReadOnlySet<string> UnloadedPageNames)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
