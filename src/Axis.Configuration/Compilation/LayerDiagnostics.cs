using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Moves the diagnostics about a layered module resource into the layer file. A diagnostic inside a part
/// the layer added or replaced goes to the path of that part in the layer. One at an element the layer changed
/// goes to the layer's path of that element. One in the module's own part whose value names an element the
/// layer removed, ignoring letter case, goes to that remove. Every other diagnostic stays in the module file.
/// </summary>
internal static class LayerDiagnostics
{
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
