using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>A module file that reads fields of an entity: its path in the release and its root, cloned so that it outlives the document.</summary>
internal sealed record FieldReader(string Path, string Entity, JsonElement Root);

/// <summary>The fields that a layer removes from a module entity, with the layer.</summary>
internal sealed record RemovedFields(LayerResource Layer, IReadOnlyList<LayerRemove> Removes);

/// <summary>
/// Refuses a layer that removes a field of a module entity while the module still reads that field. The
/// readers are the entity's contract and the module's data sources and forms on the entity that have no layer
/// of their own, because a layer of its own can stop the reading. Field names match ignoring letter case.
/// </summary>
internal static class RemovedFieldChecker
{
    private const string FieldRemovePrefix = "/changes/fields/";

    /// <summary>The removes in a merge that drop an element of the entity's <c>fields</c> list.</summary>
    internal static IReadOnlyList<LayerRemove> FieldRemoves(LayerMergeResult merge) =>
        merge.Removes.Where(remove => remove.LayerPath.StartsWith(FieldRemovePrefix, StringComparison.Ordinal)).ToList();

    /// <summary>Adds one diagnostic for each removed field that a reader still reads, naming the first reader.</summary>
    internal static void Check(
        Dictionary<string, RemovedFields> removedByEntity,
        List<FieldReader> contracts,
        List<FieldReader> dataSources,
        List<FieldReader> forms,
        List<Diagnostic> diagnostics)
    {
        foreach (var (entity, removed) in removedByEntity)
        {
            var readers = new List<(FieldReader Reader, Func<JsonElement, string, string, bool> Reads)>();
            readers.AddRange(OnEntity(contracts, entity).Select(reader => (reader, (Func<JsonElement, string, string, bool>)ContractReads)));
            readers.AddRange(OnEntity(dataSources, entity).Select(reader => (reader, (Func<JsonElement, string, string, bool>)((root, _, field) => DataSourceReads(root, field)))));
            readers.AddRange(OnEntity(forms, entity).Select(reader => (reader, (Func<JsonElement, string, string, bool>)((root, _, field) => FormReads(root, field)))));
            foreach (var remove in removed.Removes)
            {
                var first = readers.FirstOrDefault(candidate => candidate.Reads(candidate.Reader.Root, entity, remove.Key));
                if (first.Reader is null)
                {
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.RemovedFieldStillRead,
                    $"The field '{remove.Key}' is removed, but '{first.Reader.Path}' of the module still reads it. Remove it from that resource with a layer of its own, or keep the field.",
                    removed.Layer.File,
                    remove.LayerPath,
                    removed.Layer.Id));
            }
        }
    }

    private static IEnumerable<FieldReader> OnEntity(List<FieldReader> readers, string entity) =>
        readers.Where(reader => string.Equals(reader.Entity, entity, StringComparison.OrdinalIgnoreCase));

    private static bool Named(IEnumerable<string> names, string field) =>
        names.Any(name => string.Equals(name, field, StringComparison.OrdinalIgnoreCase));

    private static bool Is(string? name, string field) =>
        name is not null && string.Equals(name, field, StringComparison.OrdinalIgnoreCase);

    private static bool ContractReads(JsonElement contract, string layered, string field)
    {
        if (Object(contract, "lifecycle") is { } lifecycle)
        {
            if (Is(Text(lifecycle, "field"), field))
            {
                return true;
            }

            if (Items(lifecycle, "transitions").Any(transition => Named(ExpressionNames.NamedFields(Text(transition, "guard")), field)))
            {
                return true;
            }
        }

        if (Items(contract, "validations").Any(validation =>
            Is(Text(validation, "field"), field) || Named(ExpressionNames.NamedFields(Text(validation, "expression")), field)))
        {
            return true;
        }

        foreach (var effect in Items(contract, "effects"))
        {
            if (Object(effect, "create") is not { } create || Object(create, "values") is not { } values)
            {
                continue;
            }

            if (Is(Text(create, "entity"), layered) && values.EnumerateObject().Any(value => Is(value.Name, field)))
            {
                return true;
            }

            if (values.EnumerateObject().Any(value => value.Value.ValueKind == JsonValueKind.String
                && Named(ExpressionNames.NamedFields(value.Value.GetString()), field)))
            {
                return true;
            }
        }

        return Object(contract, "ceiling") is { } ceiling && CeilingEntries(ceiling).Any(entry => CeilingEntryReads(entry, field));
    }

    private static IEnumerable<JsonElement> CeilingEntries(JsonElement ceiling)
    {
        if (Object(ceiling, "slots") is { } slots)
        {
            foreach (var slot in slots.EnumerateObject().Where(slot => slot.Value.ValueKind == JsonValueKind.Array))
            {
                foreach (var entry in slot.Value.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object))
                {
                    yield return entry;
                }
            }
        }

        foreach (var entry in Items(ceiling, "others"))
        {
            yield return entry;
        }
    }

    private static bool CeilingEntryReads(JsonElement entry, string field) =>
        Strings(entry, "fields").Any(name => Is(name, field)) || Named(ExpressionNames.NamedFields(Text(entry, "filter")), field);

    private static bool DataSourceReads(JsonElement dataSource, string field)
    {
        if (Items(dataSource, "fields").Any(item => Is(FirstSegment(Text(item, "path")), field)))
        {
            return true;
        }

        var parameters = Items(dataSource, "parameters").Select(parameter => Text(parameter, "name")).OfType<string>().ToList();
        bool Filtered(string? filter) =>
            Named(ExpressionNames.NamedFields(filter).Where(name => !Named(parameters, name)), field);

        if (Filtered(Text(dataSource, "filter")))
        {
            return true;
        }

        return Object(dataSource, "aggregate") is { } aggregate
            && (Items(aggregate, "groupBy").Any(group => Is(Text(group, "field"), field))
                || Items(aggregate, "measures").Any(measure => Is(Text(measure, "field"), field) || Filtered(Text(measure, "filter"))));
    }

    private static bool FormReads(JsonElement form, string field) =>
        Items(form, "sections").Any(section => Items(section, "fields").Any(item => Is(Text(item, "field"), field)));

    private static string? FirstSegment(string? path) => path?.Split('.')[0];

    private static JsonElement? Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static IEnumerable<string> Strings(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)
            : [];
}
