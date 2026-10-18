using System.Diagnostics;
using System.Text.Json.Serialization;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;

namespace Axis.Server.Studio;

/// <summary>The resources a resource uses, and the resources that use it, in the order the authoring API lists them.</summary>
internal sealed record StudioReferencesResponse(IReadOnlyList<StudioReference> Uses, IReadOnlyList<StudioReference> UsedBy);

/// <summary>The entries that use a text key, which include the text resources that define it.</summary>
internal sealed record StudioTextReferencesResponse(IReadOnlyList<StudioReference> UsedBy);

/// <summary>
/// One reference. <c>File</c> and <c>Path</c> are always in the resource that holds the reference, and
/// <c>Node</c> is the other end of it.
/// </summary>
internal sealed record StudioReference(string Relation, string File, string Path, StudioReferenceNode Node);

/// <summary>
/// The other end of a reference. <c>Type</c> is <c>resource</c>, <c>text</c>, <c>package</c> or <c>module</c>, and
/// each type writes only its own fields.
/// </summary>
internal sealed record StudioReferenceNode(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Key = null);

internal static class StudioReferences
{
    public static ReferenceIndex Compile(string folder, PackageCatalog packages, ModuleCatalog modules) =>
        ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules).References;

    public static IReadOnlyList<StudioReference> Uses(IEnumerable<ReferenceEdge> edges) =>
        Entries(edges, edge => edge.To);

    public static IReadOnlyList<StudioReference> UsedBy(IEnumerable<ReferenceEdge> edges) =>
        Entries(edges, edge => edge.From);

    private static IReadOnlyList<StudioReference> Entries(IEnumerable<ReferenceEdge> edges, Func<ReferenceEdge, ReferenceNode> other) =>
        [.. edges
            .OrderBy(edge => edge.From.File, StringComparer.Ordinal)
            .ThenBy(edge => edge.Path, StringComparer.Ordinal)
            .Select(edge => new StudioReference(edge.Relation, edge.From.File, edge.Path, Map(other(edge))))];

    private static StudioReferenceNode Map(ReferenceNode node) => node switch
    {
        ResourceNode resource => new StudioReferenceNode(
            "resource", Id: resource.Id.ToString("D"), Kind: resource.Kind, Name: resource.Name, File: resource.File),
        TextKeyNode text => new StudioReferenceNode("text", Key: text.Key),
        PackageNode package => new StudioReferenceNode("package", Id: package.Id),
        ModuleNode module => new StudioReferenceNode("module", Id: module.Id),
        _ => throw new UnreachableException(),
    };
}
