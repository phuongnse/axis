using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Loading;

namespace Axis.Configuration.Compilation;

/// <summary>An element that a layer removes: the pointer of its list in the edited resource and its key as the module spells it.</summary>
internal sealed record LayerRemovedElement(string ListPath, string Key);

/// <summary>
/// The layer that turns a module resource into an edited copy of it. <see cref="Changes"/> is the layer's
/// <c>changes</c>, or null when nothing differs. <see cref="Added"/> and <see cref="Changed"/> are JSON Pointers into
/// the edited resource: an added element or property, and a changed element or a replaced property, each as a
/// whole. An object or list that changed only inside is not listed itself. <see cref="Removed"/> are the elements
/// the module has and the edit lacks. <see cref="Refusal"/> says why the edit cannot be a layer, naming the pointer
/// of the cause. On a refusal, <see cref="Changes"/> is null and the lists are empty.
/// </summary>
internal sealed record LayerDifferenceResult(
    JsonObject? Changes,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Changed,
    IReadOnlyList<LayerRemovedElement> Removed,
    string? Refusal);

/// <summary>
/// The inverse of <see cref="LayerMerger"/>: computes the smallest layer whose merge into a module resource gives
/// an edited copy back. Lists that <see cref="ListKeys"/> keys are compared element by element by their key, the
/// way the merge reads them, and every other value is replaced whole when it differs. In a list where order counts,
/// the elements that are added or moved get <c>after</c> or <c>before</c>, so that the merge gives the edited order.
/// In a keyed list where order does not count, the order of the edited copy is ignored, because a layer cannot
/// express it.
/// An edit that a layer cannot express is refused: a change to the root <c>id</c>, <c>kind</c>, <c>name</c> or
/// <c>formatVersion</c>, a property of the module that the copy drops, a keyed element with no key or a repeated
/// key, a key whose letter case alone changed, and an element of an ordered list that holds its own <c>after</c>
/// or <c>before</c>.
/// </summary>
internal static class LayerDifference
{
    /// <summary>Compares <paramref name="edited"/> with <paramref name="module"/>, a resource of <paramref name="kind"/>.</summary>
    public static LayerDifferenceResult Compare(string kind, JsonElement module, JsonElement edited)
    {
        var comparison = new Comparison(kind);
        try
        {
            var changes = comparison.CompareRoot(module, edited);
            return new LayerDifferenceResult(changes, comparison.Added, comparison.Changed, comparison.Removed, null);
        }
        catch (RefusedException refusal)
        {
            return new LayerDifferenceResult(null, [], [], [], refusal.Message);
        }
    }

    private sealed class RefusedException(string message) : Exception(message);

    private sealed class Comparison(string kind)
    {
        public List<string> Added { get; } = [];

        public List<string> Changed { get; } = [];

        public List<LayerRemovedElement> Removed { get; } = [];

        public JsonObject? CompareRoot(JsonElement module, JsonElement edited)
        {
            if (module.ValueKind != JsonValueKind.Object || edited.ValueKind != JsonValueKind.Object)
            {
                throw Refuse("", "A resource is an object.");
            }

            foreach (var name in LayerMerger.IdentityProperties)
            {
                var inModule = module.TryGetProperty(name, out var moduleValue);
                var inEdited = edited.TryGetProperty(name, out var editedValue);
                if (inModule != inEdited || (inModule && !JsonElement.DeepEquals(moduleValue, editedValue)))
                {
                    throw Refuse("/" + name, $"The '{name}' of a module resource cannot change.");
                }
            }

            var changes = CompareObject(module, edited, "", LayerMerger.IdentityProperties);
            return changes.Count == 0 ? null : changes;
        }

        private JsonObject CompareObject(JsonElement module, JsonElement edited, string pointer, string[] skipped)
        {
            foreach (var property in module.EnumerateObject())
            {
                if (Array.IndexOf(skipped, property.Name) < 0 && !edited.TryGetProperty(property.Name, out _))
                {
                    throw Refuse(Segment(pointer, property.Name), $"The edit drops the property '{property.Name}' of the module. A layer can change a property but not drop it.");
                }
            }

            var result = new JsonObject();
            foreach (var property in edited.EnumerateObject())
            {
                if (Array.IndexOf(skipped, property.Name) >= 0)
                {
                    continue;
                }

                var path = Segment(pointer, property.Name);
                if (!module.TryGetProperty(property.Name, out var current))
                {
                    result[property.Name] = Clone(property.Value);
                    Added.Add(path);
                }
                else if (current.ValueKind == JsonValueKind.Object && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var child = CompareObject(current, property.Value, path, []);
                    if (child.Count > 0)
                    {
                        result[property.Name] = child;
                    }
                }
                else if (current.ValueKind == JsonValueKind.Array
                    && property.Value.ValueKind == JsonValueKind.Array
                    && ListKeys.TryGet(kind, property.Name, out var listKey)
                    && listKey.Mode == ListKeyMode.Keyed)
                {
                    if (CompareList(current, property.Value, listKey, path) is { } list)
                    {
                        result[property.Name] = list;
                    }
                }
                else if (!JsonElement.DeepEquals(current, property.Value))
                {
                    result[property.Name] = Clone(property.Value);
                    Changed.Add(path);
                }
            }

            return result;
        }

        private JsonArray? CompareList(JsonElement module, JsonElement edited, ListKey listKey, string listPath)
        {
            if (JsonElement.DeepEquals(module, edited))
            {
                return null;
            }

            var keyProperty = listKey.Key!;
            var moduleElements = module.EnumerateArray().ToList();
            var editedElements = edited.EnumerateArray().ToList();
            var moduleKeys = KeysOf(moduleElements, keyProperty, listPath);
            var editedKeys = KeysOf(editedElements, keyProperty, listPath);
            var moduleIndexByKey = IndexByKey(moduleKeys);
            var editedKeySet = new HashSet<string>(editedKeys, StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < editedElements.Count; index++)
            {
                var element = editedElements[index];
                var elementPath = $"{listPath}/{index}";
                if (moduleIndexByKey.TryGetValue(editedKeys[index], out var moduleIndex)
                    && !string.Equals(moduleKeys[moduleIndex], editedKeys[index], StringComparison.Ordinal))
                {
                    throw Refuse(KeyPath(element, elementPath, keyProperty), $"The edit changes only the letter case of the {keyProperty} '{moduleKeys[moduleIndex]}'. A layer keeps the module's spelling.");
                }

                if (listKey.Ordered && element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { LayerMerger.AfterProperty, LayerMerger.BeforeProperty })
                    {
                        if (element.TryGetProperty(name, out _))
                        {
                            throw Refuse(Segment(elementPath, name), $"An element of this list cannot hold a '{name}' property, because a layer reads it as a place.");
                        }
                    }
                }
            }

            var placements = listKey.Ordered
                ? PlacementsOf(moduleKeys, editedKeys, editedKeySet, moduleIndexByKey)
                : new Dictionary<int, (string Anchor, bool After)>();

            var output = new JsonArray();
            for (var index = 0; index < editedElements.Count; index++)
            {
                var element = editedElements[index];
                var elementPath = $"{listPath}/{index}";
                placements.TryGetValue(index, out var placement);
                var placed = placement.Anchor is not null;
                if (!moduleIndexByKey.TryGetValue(editedKeys[index], out var moduleIndex))
                {
                    var added = Clone(element);
                    if (placed)
                    {
                        Place(added, placement, elementPath);
                    }

                    output.Add(added);
                    Added.Add(elementPath);
                    continue;
                }

                var moduleElement = moduleElements[moduleIndex];
                if (moduleElement.ValueKind == JsonValueKind.Object && element.ValueKind == JsonValueKind.Object)
                {
                    var firstChanged = Changed.Count;
                    var difference = CompareObject(moduleElement, element, elementPath, [keyProperty]);
                    if (difference.Count == 0 && !placed)
                    {
                        continue;
                    }

                    var changed = new JsonObject { [keyProperty] = editedKeys[index] };
                    foreach (var (name, value) in difference.ToList())
                    {
                        difference.Remove(name);
                        changed[name] = value;
                    }

                    if (placed)
                    {
                        Place(changed, placement, elementPath);
                    }

                    output.Add(changed);
                    Changed.Insert(firstChanged, elementPath);
                }
                else if (placed)
                {
                    throw Refuse(elementPath, "A layer cannot place this element. It is not an object that the layer can change.");
                }
                else if (!JsonElement.DeepEquals(moduleElement, element))
                {
                    output.Add(Clone(element));
                    Changed.Add(elementPath);
                }
            }

            for (var index = 0; index < moduleElements.Count; index++)
            {
                if (editedKeySet.Contains(moduleKeys[index]))
                {
                    continue;
                }

                output.Add(new JsonObject { [keyProperty] = moduleKeys[index], [LayerMerger.RemoveProperty] = true });
                Removed.Add(new LayerRemovedElement(listPath, moduleKeys[index]));
            }

            return output.Count == 0 ? null : output;
        }

        /// <summary>
        /// The place of each element of an ordered list that must move for the merge to give the edited order. The merge
        /// starts from the module's elements without the removed ones, followed by the added ones in edited order. The
        /// elements that stay put are the longest run of them that is already in edited order, and every other element is
        /// placed behind its left neighbour in the edited list, or in front of the merged list's first element.
        /// </summary>
        private static Dictionary<int, (string Anchor, bool After)> PlacementsOf(
            string[] moduleKeys,
            string[] editedKeys,
            HashSet<string> editedKeySet,
            Dictionary<string, int> moduleIndexByKey)
        {
            var placements = new Dictionary<int, (string Anchor, bool After)>();
            var editedIndexByKey = IndexByKey(editedKeys);
            var start = moduleKeys
                .Where(editedKeySet.Contains)
                .Concat(editedKeys.Where(key => !moduleIndexByKey.ContainsKey(key)))
                .ToArray();
            var stays = LongestIncreasing(start.Select(key => editedIndexByKey[key]).ToArray());
            for (var index = 0; index < editedKeys.Length; index++)
            {
                if (stays.Contains(index))
                {
                    continue;
                }

                if (index > 0)
                {
                    placements[index] = (editedKeys[index - 1], true);
                }
                else
                {
                    var first = start.First(key => !string.Equals(key, editedKeys[0], StringComparison.OrdinalIgnoreCase));
                    placements[index] = (first, false);
                }
            }

            return placements;
        }

        /// <summary>The values of the longest strictly increasing subsequence of <paramref name="values"/>.</summary>
        private static HashSet<int> LongestIncreasing(int[] values)
        {
            var length = new int[values.Length];
            var previous = new int[values.Length];
            var best = -1;
            for (var index = 0; index < values.Length; index++)
            {
                length[index] = 1;
                previous[index] = -1;
                for (var earlier = 0; earlier < index; earlier++)
                {
                    if (values[earlier] < values[index] && length[earlier] + 1 > length[index])
                    {
                        length[index] = length[earlier] + 1;
                        previous[index] = earlier;
                    }
                }

                if (best < 0 || length[index] > length[best])
                {
                    best = index;
                }
            }

            var result = new HashSet<int>();
            for (var index = best; index >= 0; index = previous[index])
            {
                result.Add(values[index]);
            }

            return result;
        }

        private static void Place(JsonNode element, (string Anchor, bool After) placement, string elementPath)
        {
            if (element is not JsonObject obj)
            {
                throw Refuse(elementPath, "A layer cannot place this element. It is not an object that the layer can change.");
            }

            obj[placement.After ? LayerMerger.AfterProperty : LayerMerger.BeforeProperty] = placement.Anchor;
        }

        /// <summary>The key of each element, refusing an element without one and a key that an earlier element has, ignoring letter case.</summary>
        private static string[] KeysOf(List<JsonElement> elements, string keyProperty, string listPath)
        {
            var keys = new string[elements.Count];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < elements.Count; index++)
            {
                var elementPath = $"{listPath}/{index}";
                var key = LayerMerger.KeyOf(elements[index], keyProperty);
                if (key is null)
                {
                    throw Refuse(KeyPath(elements[index], elementPath, keyProperty), $"An element of this list has no {keyProperty}. A layer finds an element by its {keyProperty}.");
                }

                if (!seen.Add(key))
                {
                    throw Refuse(KeyPath(elements[index], elementPath, keyProperty), $"The {keyProperty} '{key}' repeats an earlier element of the list, ignoring letter case. A layer finds an element by its {keyProperty}.");
                }

                keys[index] = key;
            }

            return keys;
        }

        private static Dictionary<string, int> IndexByKey(string[] keys)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < keys.Length; index++)
            {
                result[keys[index]] = index;
            }

            return result;
        }

        private static string KeyPath(JsonElement element, string elementPath, string keyProperty) =>
            element.ValueKind == JsonValueKind.Object ? Segment(elementPath, keyProperty) : elementPath;

        private static string Segment(string pointer, string name) => pointer + "/" + ModuleChecker.Escape(name);

        private static JsonNode Clone(JsonElement value) => JsonNode.Parse(value.GetRawText())!;

        private static RefusedException Refuse(string pointer, string reason) =>
            new($"{(pointer.Length == 0 ? "/" : pointer)}: {reason}");
    }
}
