using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;
using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>
/// The application's customization layer on one module resource, for a Studio read: the module resource with the layer
/// applied, which layer applied, and what it added, changed and removed. The marks and the removed elements come from
/// <see cref="LayerDifference"/>, the code that saving will use, so they agree with the layer that is saved. A layer with
/// errors of its own is applied as far as the merge gets, and no diagnostics are reported here. <see cref="Reset"/> puts
/// one element of the layer's resource back as the module has it.
/// </summary>
internal static class StudioModuleLayers
{
    private const string LayerFolderPrefix = "customizations/";

    /// <summary>The answer for <paramref name="found"/> in a module whose layer files are among those of <paramref name="folder"/>.</summary>
    public static StudioModuleResourceResponse Read(string folder, string module, StudioEndpoints.ModuleFile found)
    {
        var file = $"modules/{module}/{found.Path}";
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(found.Text)));
        if (Resolve(folder, module, found) is not { } state)
        {
            return Answer(found, file, version, found.Root, null, [], []);
        }

        var layerInfo = new StudioModuleLayer(state.File.Id.ToString("D"), state.File.File, state.File.Version);
        if (state.RemovesResource)
        {
            return Answer(found, file, version, found.Root, layerInfo, [], []);
        }

        var merged = state.Merged;
        var difference = LayerDifference.Compare(found.Kind, found.Root, merged);
        if (difference.Refusal is not null)
        {
            return Answer(found, file, version, merged, layerInfo, [], []);
        }

        var marks = difference.Added.Select(path => new StudioMark(path, "added"))
            .Concat(difference.Changed.Select(path => new StudioMark(path, "changed")))
            .ToList();
        var removed = new List<StudioRemovedElement>();
        foreach (var element in difference.Removed)
        {
            if (ModuleElement(found.Kind, found.Root, merged, element) is { } content)
            {
                removed.Add(new StudioRemovedElement(element.ListPath, element.Key, content));
            }
        }

        return Answer(found, file, version, merged, layerInfo, marks, removed);
    }

    // The layer of the resource and the resource with it applied. Null when the resource has no layer, or its file cannot
    // be read as one. A layer that removes the whole resource leaves the module's resource as the merged one.
    private static LayerState? Resolve(string folder, string module, StudioEndpoints.ModuleFile found)
    {
        if (FindLayer(folder, module, found) is not { } layerFile || ReadLayer(layerFile) is not { } layer)
        {
            return null;
        }

        if (layer.Changes.ValueKind == JsonValueKind.Object
            && layer.Changes.TryGetProperty(LayerMerger.RemoveProperty, out var remove)
            && remove.ValueKind == JsonValueKind.True)
        {
            return new LayerState(layerFile, true, found.Root);
        }

        var result = LayerMerger.Merge(found.Kind, found.Root, layer, new List<Diagnostic>());
        return new LayerState(layerFile, false, JsonSerializer.SerializeToElement(result.Merged));
    }

    private sealed record LayerState(StudioFile File, bool RemovesResource, JsonElement Merged);

    /// <summary>
    /// The resource with the layer applied and the element of <paramref name="key"/> in the list at the pointer
    /// <paramref name="list"/> put back as the module has it: a changed element gets the module's version in the
    /// module's position, an added element is dropped and a removed element comes back. In a text resource, <paramref name="list"/>
    /// may be <c>/texts</c> and <paramref name="key"/> a text key: a changed text gets the module's text back and an added
    /// text is dropped. The result is the edit to compare
    /// with the module for the layer that is left. <paramref name="ifMatch"/> must be the layer's version.
    /// </summary>
    public static StudioModuleReset Reset(
        string folder, string module, StudioEndpoints.ModuleFile found, string list, string key, string ifMatch)
    {
        if (FindLayer(folder, module, found) is not { } layerFile
            || !string.Equals(StudioWrites.Unquote(ifMatch), layerFile.Version, StringComparison.Ordinal))
        {
            return Refused(StudioModuleResetRefusal.Stale);
        }

        if (Resolve(folder, module, found) is not { } state)
        {
            return Refused(StudioModuleResetRefusal.Unreadable);
        }

        if (state.RemovesResource)
        {
            return Refused(StudioModuleResetRefusal.WholeResource);
        }

        if (LayerDifference.Compare(found.Kind, found.Root, state.Merged).Refusal is not null)
        {
            return Refused(StudioModuleResetRefusal.Unreadable);
        }

        if (found.Kind == ResourceKinds.Text && list == "/texts")
        {
            return ResetText(found.Root, state.Merged, key);
        }

        if (WalkList(found.Kind, found.Root, state.Merged, list) is not { } target)
        {
            return Refused(StudioModuleResetRefusal.NotFound);
        }

        var keyProperty = target.KeyProperty;
        var moduleKeys = target.ModuleList is { } moduleList
            ? moduleList.EnumerateArray().Select(element => LayerMerger.KeyOf(element, keyProperty)).ToList()
            : [];
        var moduleIndex = moduleKeys.FindIndex(moduleKey => Same(moduleKey, key));
        var mergedIndex = IndexOf(target.MergedList, keyProperty, key);
        if (mergedIndex < 0 && moduleIndex < 0)
        {
            return Refused(StudioModuleResetRefusal.NotFound);
        }

        if (mergedIndex >= 0)
        {
            target.MergedList.RemoveAt(mergedIndex);
        }

        if (moduleIndex >= 0)
        {
            // After the merged element whose key is the nearest earlier key of the module that is still there.
            var position = 0;
            for (var earlier = moduleIndex - 1; earlier >= 0 && position == 0; earlier--)
            {
                position = IndexOf(target.MergedList, keyProperty, moduleKeys[earlier]) + 1;
            }

            target.MergedList.Insert(position, JsonNode.Parse(target.ModuleList!.Value[moduleIndex].GetRawText()));
        }

        return new StudioModuleReset(JsonSerializer.SerializeToElement(target.Root), null);
    }

    // A text's `texts` is a map, not a keyed list. Its keys are matched exactly, as JSON property names are.
    private static StudioModuleReset ResetText(JsonElement module, JsonElement merged, string key)
    {
        var root = JsonNode.Parse(merged.GetRawText());
        if (root?["texts"] is not JsonObject texts)
        {
            return Refused(StudioModuleResetRefusal.NotFound);
        }

        var moduleHas = module.TryGetProperty("texts", out var moduleTexts)
            && moduleTexts.ValueKind == JsonValueKind.Object
            && moduleTexts.TryGetProperty(key, out _);
        if (!texts.ContainsKey(key) && !moduleHas)
        {
            return Refused(StudioModuleResetRefusal.NotFound);
        }

        if (moduleHas)
        {
            texts[key] = JsonNode.Parse(moduleTexts.GetProperty(key).GetRawText());
        }
        else
        {
            texts.Remove(key);
        }

        return new StudioModuleReset(JsonSerializer.SerializeToElement(root), null);
    }

    private static StudioModuleReset Refused(StudioModuleResetRefusal refusal) => new(null, refusal);

    private static int IndexOf(JsonArray list, string keyProperty, string? key) =>
        list.ToList().FindIndex(element => Same(KeyOf(element, keyProperty), key));

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? KeyOf(JsonNode? node, string keyProperty) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonObject obj when obj[keyProperty] is JsonValue key && key.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    private sealed record ListTarget(JsonNode Root, JsonArray MergedList, JsonElement? ModuleList, string KeyProperty);

    // Walks the list's pointer over a copy of the merged resource and the module's together, as ModuleElement does. The
    // pointer must end at a keyed list. A module list that cannot be reached, such as one under an added element, is empty.
    private static ListTarget? WalkList(string kind, JsonElement module, JsonElement merged, string list)
    {
        var root = JsonNode.Parse(merged.GetRawText());
        var current = root;
        JsonElement? currentModule = module;
        string? property = null;
        foreach (var raw in list.Split('/').Skip(1))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out current) || current is null)
                {
                    return null;
                }

                currentModule = currentModule is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(segment, out var child)
                    ? child
                    : null;
                property = segment;
            }
            else if (current is JsonArray array
                && property is not null
                && int.TryParse(segment, out var index)
                && index >= 0
                && index < array.Count
                && ListKeys.TryGet(kind, property, out var listKey)
                && listKey.Key is not null
                && KeyOf(array[index], listKey.Key) is { } key)
            {
                current = array[index];
                currentModule = currentModule is { ValueKind: JsonValueKind.Array } moduleArray
                    ? ByKey(moduleArray, listKey.Key, key)
                    : null;
            }
            else
            {
                return null;
            }
        }

        return root is not null
            && current is JsonArray mergedList
            && property is not null
            && ListKeys.TryGet(kind, property, out var last)
            && last.Key is not null
            ? new ListTarget(root, mergedList, currentModule is { ValueKind: JsonValueKind.Array } ? currentModule : null, last.Key)
            : null;
    }

    private static StudioModuleResourceResponse Answer(
        StudioEndpoints.ModuleFile found,
        string file,
        string version,
        JsonElement content,
        StudioModuleLayer? layer,
        IReadOnlyList<StudioMark> marks,
        IReadOnlyList<StudioRemovedElement> removed) =>
        new(found.Id.ToString("D"), found.Kind, found.Name, file, version, content, layer, marks, removed);

    // The layer file of the resource: customizations/<module>/<name>.json, ignoring letter case in the name. A scenario
    // has none, because compilation never applies a layer to a scenario.
    private static StudioFile? FindLayer(string folder, string module, StudioEndpoints.ModuleFile found)
    {
        if (found.Name is null || found.Kind == ResourceKinds.Scenario)
        {
            return null;
        }

        var prefix = $"{LayerFolderPrefix}{module}/";
        var expected = $"{found.Name}.json";
        return StudioFolder.List(folder).FirstOrDefault(file =>
            file.Kind == ResourceKinds.Layer
            && file.File.StartsWith(prefix, StringComparison.Ordinal)
            && string.Equals(file.File[prefix.Length..], expected, StringComparison.OrdinalIgnoreCase));
    }

    private static LayerResource? ReadLayer(StudioFile file)
    {
        try
        {
            using var document = JsonDocument.Parse(file.Bytes);
            var root = document.RootElement;
            var changes = root.TryGetProperty("changes", out var value) && value.ValueKind == JsonValueKind.Object
                ? value.Clone()
                : JsonSerializer.SerializeToElement(new { });
            return new LayerResource
            {
                Id = file.Id,
                Kind = ResourceKinds.Layer,
                FormatVersion = root.TryGetProperty("formatVersion", out var format) && format.TryGetInt32(out var number) ? number : 1,
                Target = root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String ? target.GetString()! : "",
                Changes = changes,
                File = file.File,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The module's element that the layer removed: walks the list's pointer over the merged resource and the module's
    // together. An index segment finds the module's element by the key of the merged one.
    private static JsonElement? ModuleElement(string kind, JsonElement module, JsonElement merged, LayerRemovedElement removed)
    {
        var current = module;
        var currentMerged = merged;
        string? property = null;
        foreach (var raw in removed.ListPath.Split('/').Skip(1))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (currentMerged.ValueKind == JsonValueKind.Object && current.ValueKind == JsonValueKind.Object)
            {
                if (!currentMerged.TryGetProperty(segment, out currentMerged) || !current.TryGetProperty(segment, out current))
                {
                    return null;
                }

                property = segment;
            }
            else if (currentMerged.ValueKind == JsonValueKind.Array
                && current.ValueKind == JsonValueKind.Array
                && property is not null
                && int.TryParse(segment, out var index)
                && index >= 0
                && index < currentMerged.GetArrayLength()
                && ListKeys.TryGet(kind, property, out var listKey)
                && listKey.Key is not null
                && LayerMerger.KeyOf(currentMerged[index], listKey.Key) is { } key)
            {
                currentMerged = currentMerged[index];
                if (ByKey(current, listKey.Key, key) is not { } element)
                {
                    return null;
                }

                current = element;
            }
            else
            {
                return null;
            }
        }

        return property is not null && ListKeys.TryGet(kind, property, out var last) && last.Key is not null
            ? ByKey(current, last.Key, removed.Key)
            : null;
    }

    private static JsonElement? ByKey(JsonElement list, string keyProperty, string key)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var element in list.EnumerateArray())
        {
            if (string.Equals(LayerMerger.KeyOf(element, keyProperty), key, StringComparison.OrdinalIgnoreCase))
            {
                return element.Clone();
            }
        }

        return null;
    }
}

/// <summary>Why a reset of one element cannot go ahead: the layer's version is stale or there is no layer, the layer removes the whole resource, the layer cannot be read back, or no element has the key.</summary>
internal enum StudioModuleResetRefusal
{
    Stale,
    WholeResource,
    Unreadable,
    NotFound,
}

/// <summary>The resource with the element put back, or why that cannot be done.</summary>
internal sealed record StudioModuleReset(JsonElement? Edited, StudioModuleResetRefusal? Refusal);
