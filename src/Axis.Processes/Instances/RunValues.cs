using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;

namespace Axis.Processes.Instances;

/// <summary>
/// The subject field values that a step run keeps in its history row, see
/// <see href="docs/reference/process-inspection.md#instance-detail">process inspection</see> and
/// <see href="docs/reference/process-tables.md#step-history">process tables</see>. A field that the pinned release marks sensitive
/// or a file field keeps its name and no value, so no plaintext copy of it and no file name exists. Inspection hides each value over its own field when the
/// detail is read.
/// </summary>
internal static class RunValues
{
    /// <summary>
    /// The subject fields that the conditions of <paramref name="step"/>'s branches name, as <c>{ "name", "value" }</c> in the
    /// entity's field order, with the values in <paramref name="record"/>, the record the conditions saw. A reference path gives
    /// the reference field, and a child collection gives no entry. A branch whose condition cannot be resolved adds no entry.
    /// </summary>
    public static JsonArray Read(ApplicationModel pinned, EntityModel entity, DecisionStepModel step, Record record)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var branch in step.Branches)
        {
            if (MappingFields.Collect(pinned, entity, branch.When) is { } fields)
            {
                named.UnionWith(fields.Where(field => field.EntityId == entity.Id).Select(field => field.Field));
            }
        }

        var result = new JsonArray();
        foreach (var field in entity.Fields.Where(field => field.Type != FieldType.ChildCollection && named.Contains(field.Name)))
        {
            var entry = new JsonObject { ["name"] = field.Name };
            if (KeepsValue(pinned, field))
            {
                entry["value"] = record.Values.GetValueOrDefault(field.Name)?.DeepClone();
            }

            result.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// The subject fields whose stored value differs between <paramref name="before"/> and <paramref name="after"/>, as
    /// <c>{ "field", "before", "after" }</c> in the entity's field order. It uses the helper that data change triggers use, so
    /// a computed field that the write recomputed counts. It is an empty array when nothing differed.
    /// </summary>
    public static JsonArray Changes(ApplicationModel pinned, EntityModel entity, Record before, Record after)
    {
        var changed = DataChangeTriggerStarts.ChangedFields(entity, before, after, []);
        var result = new JsonArray();
        foreach (var field in entity.Fields.Where(field => changed.Contains(field.Name)))
        {
            var entry = new JsonObject { ["field"] = field.Name };
            if (KeepsValue(pinned, field))
            {
                entry["before"] = before.Values.GetValueOrDefault(field.Name)?.DeepClone();
                entry["after"] = after.Values.GetValueOrDefault(field.Name)?.DeepClone();
            }

            result.Add(entry);
        }

        return result;
    }

    // A file field holds the file's name, which history never stores, see docs/reference/file-fields.md#storage-and-clean-up.
    private static bool KeepsValue(ApplicationModel pinned, FieldModel field) =>
        field.Type != FieldType.File && !ProcessResults.IsSensitive(pinned, field);
}
