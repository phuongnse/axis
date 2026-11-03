using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;
using Json.Schema;

namespace Axis.Configuration.Compilation;

/// <summary>An element that a layer removes, by its key, with the path of the remove in the layer file.</summary>
internal sealed record LayerRemove(string Key, string LayerPath);

/// <summary>
/// The module's resource with a layer's changes applied, and where each change came from. By the JSON
/// Pointer a value has in <see cref="Merged"/>: <see cref="LayerPathByMergedPath"/> holds the path in the layer
/// file of each value that the layer added or replaced, whole subtree included, and
/// <see cref="ChangedPathByMergedPath"/> the path of each object, list or element that the layer changed in place.
/// <see cref="Removes"/> are the elements the layer removed.
/// </summary>
internal sealed record LayerMergeResult(
    JsonNode Merged,
    IReadOnlyDictionary<string, string> LayerPathByMergedPath,
    IReadOnlyDictionary<string, string> ChangedPathByMergedPath,
    IReadOnlyList<LayerRemove> Removes);

/// <summary>
/// The module version that a layer applied to before, when the application's pin moves to another version: the
/// module and both versions, and the root of the same resource in the old version.
/// </summary>
internal sealed record LayerUpgrade(string ModuleId, string FromVersion, string ToVersion, JsonElement Previous);

/// <summary>
/// Applies a <c>layer</c>'s <c>changes</c> to a module resource. Objects merge property by property. A list
/// that <see cref="ListKeys"/> keys merges element by element: an element with a key the module's list has
/// changes that element, one without it is added at the end, and one with <c>"remove": true</c> is dropped.
/// In a list where order counts, an added or changed element with <c>after</c> or <c>before</c> then moves
/// right behind or in front of the element that names, one placement at a time in the layer's order, and the
/// merged resource does not hold <c>after</c> or <c>before</c>.
/// Every other value, value lists and lists still pending included, is replaced whole. The root
/// <c>id</c>, <c>kind</c>, <c>name</c> and <c>formatVersion</c>, and the key of an element, never change.
/// </summary>
internal static class LayerMerger
{
    internal const string RemoveProperty = "remove";
    internal const string AfterProperty = "after";
    internal const string BeforeProperty = "before";

    internal static readonly string[] IdentityProperties = ["id", "kind", "name", "formatVersion"];

    private static readonly string[] _placementProperties = [AfterProperty, BeforeProperty];

    /// <summary>
    /// Merges the changes of <paramref name="layer"/> into <paramref name="module"/>, a resource of
    /// <paramref name="kind"/>. A remove of an unknown key and a remove that holds more than its key are
    /// added to <paramref name="diagnostics"/>, and the merge goes on without them. So are a placement with both
    /// <c>after</c> and <c>before</c> and one whose anchor names no element of the merged list.
    /// With an <paramref name="upgrade"/>, a changed element whose key the new version lacks but the old version
    /// has is reported as <see cref="DiagnosticCodes.LayerKeyDroppedByModuleUpgrade"/> and left out, because the
    /// layer meant to change it and would add a broken copy instead.
    /// </summary>
    public static LayerMergeResult Merge(string kind, JsonElement module, LayerResource layer, List<Diagnostic> diagnostics, LayerUpgrade? upgrade = null)
    {
        var merged = JsonNode.Parse(module.GetRawText())!;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new Dictionary<string, string>(StringComparer.Ordinal);
        var removes = new List<LayerRemove>();
        if (merged is JsonObject root && layer.Changes.ValueKind == JsonValueKind.Object)
        {
            new Merging(kind, layer, map, changed, removes, diagnostics, upgrade).MergeObject(root, layer.Changes, "", "/changes", IdentityProperties, upgrade?.Previous);
        }

        return new LayerMergeResult(merged, map, changed, removes);
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

    private sealed class Merging(
        string kind,
        LayerResource layer,
        Dictionary<string, string> map,
        Dictionary<string, string> changed,
        List<LayerRemove> removes,
        List<Diagnostic> diagnostics,
        LayerUpgrade? upgrade)
    {
        public void MergeObject(JsonObject target, JsonElement change, string mergedPath, string layerPath, string[] skipped, JsonElement? previous)
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
                JsonElement? previousChild = previous is { ValueKind: JsonValueKind.Object } previousObject
                    && previousObject.TryGetProperty(property.Name, out var found)
                    ? found
                    : null;
                if (current is JsonObject currentObject && property.Value.ValueKind == JsonValueKind.Object)
                {
                    changed[propertyMergedPath] = propertyLayerPath;
                    MergeObject(currentObject, property.Value, propertyMergedPath, propertyLayerPath, [], previousChild);
                }
                else if (current is JsonArray currentList
                    && property.Value.ValueKind == JsonValueKind.Array
                    && ListKeys.TryGet(kind, property.Name, out var listKey)
                    && listKey.Mode == ListKeyMode.Keyed)
                {
                    changed[propertyMergedPath] = propertyLayerPath;
                    target[property.Name] = MergeList(currentList, property.Value, listKey, propertyMergedPath, propertyLayerPath, previousChild);
                }
                else
                {
                    map[propertyMergedPath] = propertyLayerPath;
                    target[property.Name] = Replace(property.Value, propertyMergedPath, propertyLayerPath);
                }
            }
        }

        private JsonArray MergeList(JsonArray module, JsonElement change, ListKey listKey, string mergedPath, string layerPath, JsonElement? previous)
        {
            var keyProperty = listKey.Key!;
            var previousElements = previous is { ValueKind: JsonValueKind.Array } previousList
                ? previousList.EnumerateArray().ToList()
                : [];
            var placements = new List<Placement>();
            var entries = module
                .Select(node => new Entry(KeyOf(node, keyProperty), node?.DeepClone()) { Previous = PreviousOf(previousElements, KeyOf(node, keyProperty), keyProperty) })
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
                        removes.Add(new LayerRemove(key!, elementLayerPath));
                        entries.RemoveAt(position);
                    }
                }
                else if (position < 0 && key is not null && PreviousOf(previousElements, key, keyProperty) is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LayerKeyDroppedByModuleUpgrade,
                        $"Version {upgrade?.ToVersion} of the module '{upgrade?.ModuleId}' has no element with the {keyProperty} '{key}' that version {upgrade?.FromVersion} has and this layer changes.",
                        layer.File,
                        $"{elementLayerPath}/{ModuleChecker.Escape(keyProperty)}",
                        layer.Id));
                }
                else
                {
                    Entry entry;
                    if (position >= 0)
                    {
                        entry = entries[position];
                        entry.Pieces.Add((element, index));
                    }
                    else
                    {
                        entry = new Entry(key, null) { Pieces = { (element, index) } };
                        entries.Add(entry);
                    }

                    if (listKey.Ordered && ReadPlacement(element, entry, elementLayerPath) is { } placement)
                    {
                        placements.Add(placement);
                    }
                }

                index++;
            }

            foreach (var placement in placements)
            {
                Place(entries, placement);
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
                        if (listKey.Ordered && node is JsonObject added)
                        {
                            foreach (var name in _placementProperties)
                            {
                                added.Remove(name);
                                map.Remove($"{elementMergedPath}/{name}");
                            }
                        }
                    }
                    else if (node is JsonObject nodeObject && piece.ValueKind == JsonValueKind.Object)
                    {
                        changed[elementMergedPath] = pieceLayerPath;
                        MergeObject(nodeObject, piece, elementMergedPath, pieceLayerPath, listKey.Ordered ? [keyProperty, AfterProperty, BeforeProperty] : [keyProperty], entry.Previous);
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

        /// <summary>Reads the <c>after</c> or <c>before</c> of an element, or reports an element that has both.</summary>
        private Placement? ReadPlacement(JsonElement element, Entry entry, string elementLayerPath)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var hasAfter = element.TryGetProperty(AfterProperty, out var after);
            var hasBefore = element.TryGetProperty(BeforeProperty, out var before);
            if (hasAfter && hasBefore)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.LayerAfterAndBefore,
                    "An element places itself with either \"after\" or \"before\", never both.",
                    layer.File,
                    elementLayerPath,
                    layer.Id));
                return null;
            }

            return hasAfter || hasBefore
                ? new Placement(entry, hasAfter ? after : before, hasAfter, elementLayerPath)
                : null;
        }

        /// <summary>Moves the entry of a placement behind or in front of its anchor, or reports an anchor that names no other element.</summary>
        private void Place(List<Entry> entries, Placement placement)
        {
            var current = entries.IndexOf(placement.Entry);
            if (current < 0)
            {
                return;
            }

            var anchorKey = placement.Anchor.ValueKind == JsonValueKind.String ? placement.Anchor.GetString() : null;
            var anchor = anchorKey is null
                ? -1
                : entries.FindIndex(entry => entry != placement.Entry && string.Equals(entry.Key, anchorKey, StringComparison.OrdinalIgnoreCase));
            if (anchor < 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownLayerAnchor,
                    $"The merged list has no other element with the key '{(anchorKey ?? placement.Anchor.ToString())}' to place the element {(placement.IsAfter ? "after" : "before")}.",
                    layer.File,
                    placement.LayerPath,
                    layer.Id));
                return;
            }

            entries.RemoveAt(current);
            if (current < anchor)
            {
                anchor--;
            }

            entries.Insert(placement.IsAfter ? anchor + 1 : anchor, placement.Entry);
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
            map[mergedPath] = layerPath;
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

        private sealed record Placement(Entry Entry, JsonElement Anchor, bool IsAfter, string LayerPath);

        private sealed class Entry(string? key, JsonNode? node)
        {
            public string? Key { get; } = key;

            public JsonNode? Node { get; } = node;

            public List<(JsonElement Element, int LayerIndex)> Pieces { get; } = [];

            /// <summary>The element with the same key in the old version of the module, when there is one.</summary>
            public JsonElement? Previous { get; init; }
        }
    }

    /// <summary>The element of an old version's list with <paramref name="key"/>, ignoring letter case.</summary>
    private static JsonElement? PreviousOf(List<JsonElement> previousElements, string? key, string keyProperty)
    {
        if (key is null)
        {
            return null;
        }

        foreach (var element in previousElements)
        {
            if (string.Equals(KeyOf(element, keyProperty), key, StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    private static string? KeyOf(JsonNode? node, string keyProperty) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonObject obj when obj[keyProperty] is JsonValue key && key.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    internal static string? KeyOf(JsonElement element, string keyProperty) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Object when element.TryGetProperty(keyProperty, out var key) && key.ValueKind == JsonValueKind.String => key.GetString(),
        _ => null,
    };
}
