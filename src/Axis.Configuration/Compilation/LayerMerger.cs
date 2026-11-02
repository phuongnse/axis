using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;
using Json.Schema;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The module's resource with a layer's changes applied, and the path in the layer file of each property
/// that came from the layer, by the JSON Pointer the property has in <see cref="Merged"/>.
/// </summary>
internal sealed record LayerMergeResult(JsonNode Merged, IReadOnlyDictionary<string, string> LayerPathByMergedPath);

/// <summary>
/// Applies a <c>layer</c>'s <c>changes</c> to a module resource. Objects merge property by property. A list
/// that <see cref="ListKeys"/> keys merges element by element: an element with a key the module's list has
/// changes that element, one without it is added at the end, and one with <c>"remove": true</c> is dropped.
/// Every other value, value lists and lists still pending included, is replaced whole. The root
/// <c>id</c>, <c>kind</c>, <c>name</c> and <c>formatVersion</c>, and the key of an element, never change.
/// </summary>
internal static class LayerMerger
{
    private const string RemoveProperty = "remove";

    private static readonly string[] _identityProperties = ["id", "kind", "name", "formatVersion"];

    /// <summary>
    /// Merges the changes of <paramref name="layer"/> into <paramref name="module"/>, a resource of
    /// <paramref name="kind"/>. A remove of an unknown key and a remove that holds more than its key are
    /// added to <paramref name="diagnostics"/>, and the merge goes on without them.
    /// </summary>
    public static LayerMergeResult Merge(string kind, JsonElement module, LayerResource layer, List<Diagnostic> diagnostics)
    {
        var merged = JsonNode.Parse(module.GetRawText())!;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (merged is JsonObject root && layer.Changes.ValueKind == JsonValueKind.Object)
        {
            new Merging(kind, layer, map, diagnostics).MergeObject(root, layer.Changes, "", "/changes", _identityProperties);
        }

        return new LayerMergeResult(merged, map);
    }

    /// <summary>
    /// Reports <see cref="DiagnosticCodes.UnknownLayerProperty"/> at the layer's path of each property that the
    /// merged resource has and its schema does not allow, when the layer set it.
    /// </summary>
    public static void CheckProperties(JsonSchema schema, LayerMergeResult result, LayerResource layer, List<Diagnostic> diagnostics)
    {
        using var document = JsonSerializer.SerializeToDocument(result.Merged);
        foreach (var pointer in SchemaDiagnostics.RejectedProperties(schema, document.RootElement))
        {
            if (!result.LayerPathByMergedPath.TryGetValue(pointer, out var layerPath))
            {
                continue;
            }

            var property = pointer[(pointer.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnknownLayerProperty,
                $"The property '{property}' is not a property of the target '{layer.Target}'.",
                layer.File,
                layerPath,
                layer.Id));
        }
    }

    private sealed class Merging(string kind, LayerResource layer, Dictionary<string, string> map, List<Diagnostic> diagnostics)
    {
        public void MergeObject(JsonObject target, JsonElement change, string mergedPath, string layerPath, string[] skipped)
        {
            foreach (var property in change.EnumerateObject())
            {
                if (Array.IndexOf(skipped, property.Name) >= 0)
                {
                    continue;
                }

                var segment = "/" + ModuleChecker.Escape(property.Name);
                var propertyMergedPath = mergedPath + segment;
                var propertyLayerPath = layerPath + segment;
                var current = target[property.Name];
                if (current is JsonObject currentObject && property.Value.ValueKind == JsonValueKind.Object)
                {
                    MergeObject(currentObject, property.Value, propertyMergedPath, propertyLayerPath, []);
                }
                else if (current is JsonArray currentList
                    && property.Value.ValueKind == JsonValueKind.Array
                    && ListKeys.TryGet(kind, property.Name, out var listKey)
                    && listKey.Mode == ListKeyMode.Keyed)
                {
                    target[property.Name] = MergeList(currentList, property.Value, listKey.Key!, propertyMergedPath, propertyLayerPath);
                }
                else
                {
                    map[propertyMergedPath] = propertyLayerPath;
                    target[property.Name] = Replace(property.Value, propertyMergedPath, propertyLayerPath);
                }
            }
        }

        private JsonArray MergeList(JsonArray module, JsonElement change, string keyProperty, string mergedPath, string layerPath)
        {
            var entries = module
                .Select(node => new Entry(KeyOf(node, keyProperty), node?.DeepClone()))
                .ToList();
            var index = 0;
            foreach (var element in change.EnumerateArray())
            {
                var elementLayerPath = $"{layerPath}/{index}";
                var key = KeyOf(element, keyProperty);
                var position = key is null ? -1 : entries.FindIndex(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(RemoveProperty, out var remove)
                    && remove.ValueKind == JsonValueKind.True)
                {
                    CheckRemove(element, key, keyProperty, position, elementLayerPath);
                    if (position >= 0)
                    {
                        entries.RemoveAt(position);
                    }
                }
                else if (position >= 0)
                {
                    entries[position].Pieces.Add((element, index));
                }
                else
                {
                    entries.Add(new Entry(key, null) { Pieces = { (element, index) } });
                }

                index++;
            }

            var result = new JsonArray();
            for (var finalIndex = 0; finalIndex < entries.Count; finalIndex++)
            {
                var entry = entries[finalIndex];
                var elementMergedPath = $"{mergedPath}/{finalIndex}";
                var node = entry.Node;
                foreach (var (piece, pieceIndex) in entry.Pieces)
                {
                    var pieceLayerPath = $"{layerPath}/{pieceIndex}";
                    if (node is null)
                    {
                        node = Replace(piece, elementMergedPath, pieceLayerPath);
                    }
                    else if (node is JsonObject nodeObject && piece.ValueKind == JsonValueKind.Object)
                    {
                        MergeObject(nodeObject, piece, elementMergedPath, pieceLayerPath, [keyProperty]);
                    }
                    else if (!(node is JsonValue && piece.ValueKind == JsonValueKind.String && entry.Key is not null))
                    {
                        node = Replace(piece, elementMergedPath, pieceLayerPath);
                    }
                }

                result.Add(node);
            }

            return result;
        }

        private void CheckRemove(JsonElement element, string? key, string keyProperty, int position, string elementLayerPath)
        {
            if (key is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidLayerRemove,
                    $"An element with \"remove\": true must name the element to remove by its '{keyProperty}'.",
                    layer.File,
                    elementLayerPath,
                    layer.Id));
            }
            else if (position < 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownLayerKey,
                    $"The module's list has no element with the {keyProperty} '{key}', so it cannot be removed.",
                    layer.File,
                    $"{elementLayerPath}/{ModuleChecker.Escape(keyProperty)}",
                    layer.Id));
            }

            if (element.EnumerateObject().Any(property => property.Name != keyProperty && property.Name != RemoveProperty))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidLayerRemove,
                    $"An element with \"remove\": true holds only its '{keyProperty}' and 'remove'.",
                    layer.File,
                    elementLayerPath,
                    layer.Id));
            }
        }

        /// <summary>A copy of a value of the layer, noting the layer's path of each property inside it.</summary>
        private JsonNode Replace(JsonElement value, string mergedPath, string layerPath)
        {
            var node = JsonNode.Parse(value.GetRawText())!;
            Record(node, mergedPath, layerPath);
            return node;
        }

        private void Record(JsonNode? node, string mergedPath, string layerPath)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, child) in obj)
                    {
                        var segment = "/" + ModuleChecker.Escape(name);
                        map[mergedPath + segment] = layerPath + segment;
                        Record(child, mergedPath + segment, layerPath + segment);
                    }

                    break;
                case JsonArray list:
                    for (var index = 0; index < list.Count; index++)
                    {
                        Record(list[index], $"{mergedPath}/{index}", $"{layerPath}/{index}");
                    }

                    break;
            }
        }

        private sealed class Entry(string? key, JsonNode? node)
        {
            public string? Key { get; } = key;

            public JsonNode? Node { get; } = node;

            public List<(JsonElement Element, int LayerIndex)> Pieces { get; } = [];
        }
    }

    private static string? KeyOf(JsonNode? node, string keyProperty) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonObject obj when obj[keyProperty] is JsonValue key && key.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    private static string? KeyOf(JsonElement element, string keyProperty) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Object when element.TryGetProperty(keyProperty, out var key) && key.ValueKind == JsonValueKind.String => key.GetString(),
        _ => null,
    };
}
