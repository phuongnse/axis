using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;

namespace Axis.Server.Studio;

/// <summary>
/// The application's customization layer on one module resource, for a Studio read: the module resource with the layer
/// applied, which layer applied, and what it added, changed and removed. The marks and the removed elements come from
/// <see cref="LayerDifference"/>, the code that saving will use, so they agree with the layer that is saved. A layer with
/// errors of its own is applied as far as the merge gets, and no diagnostics are reported here.
/// </summary>
internal static class StudioModuleLayers
{
    private const string LayerFolderPrefix = "customizations/";

    /// <summary>The answer for <paramref name="found"/> in a module whose layer files are among those of <paramref name="folder"/>.</summary>
    public static StudioModuleResourceResponse Read(string folder, string module, StudioEndpoints.ModuleFile found)
    {
        var file = $"modules/{module}/{found.Path}";
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(found.Text)));
        if (FindLayer(folder, module, found) is not { } layerFile
            || ReadLayer(layerFile) is not { } layer)
        {
            return Answer(found, file, version, found.Root, null, [], []);
        }

        var layerInfo = new StudioModuleLayer(layerFile.Id.ToString("D"), layerFile.File, layerFile.Version);
        if (layer.Changes.ValueKind == JsonValueKind.Object
            && layer.Changes.TryGetProperty(LayerMerger.RemoveProperty, out var remove)
            && remove.ValueKind == JsonValueKind.True)
        {
            return Answer(found, file, version, found.Root, layerInfo, [], []);
        }

        var result = LayerMerger.Merge(found.Kind, found.Root, layer, new List<Diagnostic>());
        var merged = JsonSerializer.SerializeToElement(result.Merged);
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
