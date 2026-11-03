using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Moves the diagnostics about a layered module resource into the layer file. A diagnostic inside a part
/// the layer added or replaced goes to the path of that part in the layer. One at an element the layer changed
/// goes to the layer's path of that element. One in the module's own part whose value names an element the
/// layer removed, ignoring letter case, goes to that remove. Every other diagnostic stays in the module file.
/// A diagnostic in another file whose value names a whole resource that a layer removed goes to the layer's
/// <c>/changes/remove</c>.
/// </summary>
internal static class LayerDiagnostics
{
    private const string LayerFolderPrefix = "customizations/";

    public static void MapToLayers(List<Diagnostic> diagnostics, IReadOnlyDictionary<string, LayerTrace> traces)
    {
        if (traces.Count == 0)
        {
            return;
        }

        for (var index = 0; index < diagnostics.Count; index++)
        {
            var diagnostic = diagnostics[index];
            if (diagnostic.Path.Length == 0 || !traces.TryGetValue(diagnostic.File, out var trace))
            {
                continue;
            }

            if (LayerPathOf(diagnostic.Path, trace.Merge) is { } layerPath)
            {
                diagnostics[index] = diagnostic with { File = trace.Layer.File, Path = layerPath, ResourceId = trace.Layer.Id };
            }
        }
    }

    /// <summary>
    /// Moves a diagnostic about a reference to a removed module resource into the layer file, at
    /// <c>/changes/remove</c>, and names the referring file in its message. The reference is the string at the
    /// diagnostic's path in <paramref name="contentOf"/> that equals <c>&lt;module id&gt;/&lt;name&gt;</c>, or the plain
    /// name in a file of the module, ignoring letter case. A diagnostic in a layer file stays there.
    /// </summary>
    public static void MapRemovedReferences(List<Diagnostic> diagnostics, IReadOnlyList<RemovedResource> removed, Func<string, JsonNode?> contentOf)
    {
        if (removed.Count == 0)
        {
            return;
        }

        for (var index = 0; index < diagnostics.Count; index++)
        {
            var diagnostic = diagnostics[index];
            if (diagnostic.Path.Length == 0 || diagnostic.File.StartsWith(LayerFolderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (contentOf(diagnostic.File) is not { } content || ValueAt(content, diagnostic.Path) is not { } value)
            {
                continue;
            }

            var match = removed.FirstOrDefault(resource =>
                string.Equals(value, $"{resource.ModuleId}/{resource.Name}", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(value, resource.Name, StringComparison.OrdinalIgnoreCase)
                    && diagnostic.File.StartsWith($"modules/{resource.ModuleId}/", StringComparison.Ordinal)));
            if (match is not null)
            {
                diagnostics[index] = diagnostic with
                {
                    File = match.Layer.File,
                    Path = "/changes/remove",
                    ResourceId = match.Layer.Id,
                    Message = $"{diagnostic.Message} It is named in '{diagnostic.File}' at '{diagnostic.Path}', and the layer removes '{match.Layer.Target}'.",
                };
            }
        }
    }

    private static string? LayerPathOf(string path, LayerMergeResult merge)
    {
        // The deepest prefix that the layer added or replaced, so the path inside it is the layer's own.
        for (var end = path.Length; end > 0; end = path.LastIndexOf('/', end - 1))
        {
            if (merge.LayerPathByMergedPath.TryGetValue(path[..end], out var layerPath))
            {
                return layerPath + path[end..];
            }
        }

        if (merge.ChangedPathByMergedPath.TryGetValue(path, out var changedPath))
        {
            return changedPath;
        }

        if (merge.Removes.Count > 0 && ValueAt(merge.Merged, path) is { } value)
        {
            return merge.Removes.FirstOrDefault(remove => string.Equals(remove.Key, value, StringComparison.OrdinalIgnoreCase))?.LayerPath;
        }

        return null;
    }

    /// <summary>The string at a JSON Pointer of the merged resource, or null when there is none.</summary>
    private static string? ValueAt(JsonNode root, string pointer)
    {
        var node = root;
        foreach (var raw in pointer[1..].Split('/'))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            node = node switch
            {
                JsonObject obj => obj[segment],
                JsonArray list when int.TryParse(segment, out var position) && position >= 0 && position < list.Count => list[position],
                _ => null,
            };
            if (node is null)
            {
                return null;
            }
        }

        return node is JsonValue leaf && leaf.TryGetValue<string>(out var text) ? text : null;
    }
}
