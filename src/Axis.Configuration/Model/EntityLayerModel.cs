namespace Axis.Configuration.Model;

/// <summary>
/// Where a layered module entity came from in a customization layer. A diagnostic about the entity can then be
/// reported in the layer file instead of the module's file.
/// </summary>
/// <param name="File">The layer file, relative to the application folder with <c>/</c> separators.</param>
/// <param name="Id">The layer's id.</param>
/// <param name="LayerPathByMergedPath">
/// The path in the layer file of each value the layer added or replaced, by the JSON Pointer the value has in the entity's merged file.
/// </param>
/// <param name="ChangedPathByMergedPath">The path in the layer file of each object, list or element the layer changed in place.</param>
/// <param name="RemovedFieldPaths">The path in the layer file of each field the layer removed, by the field's name.</param>
public sealed record EntityLayerModel(
    string File,
    Guid Id,
    IReadOnlyDictionary<string, string> LayerPathByMergedPath,
    IReadOnlyDictionary<string, string> ChangedPathByMergedPath,
    IReadOnlyDictionary<string, string> RemovedFieldPaths)
{
    /// <summary>
    /// The path in the layer file of the value at <paramref name="mergedPath"/> of the entity's merged file, or null when
    /// the layer did not add, replace or change it.
    /// </summary>
    public string? PathOf(string mergedPath)
    {
        // The deepest prefix that the layer added or replaced, so the path inside it is the layer's own.
        for (var end = mergedPath.Length; end > 0; end = mergedPath.LastIndexOf('/', end - 1))
        {
            if (LayerPathByMergedPath.TryGetValue(mergedPath[..end], out var layerPath))
            {
                return layerPath + mergedPath[end..];
            }
        }

        return ChangedPathByMergedPath.GetValueOrDefault(mergedPath);
    }
}
