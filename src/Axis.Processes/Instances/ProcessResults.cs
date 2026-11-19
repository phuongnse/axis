using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;

namespace Axis.Processes.Instances;

/// <summary>
/// The values an instance ends with, which the engine keeps in the instance's <c>result</c> column when it completes, see
/// <see href="docs/reference/process-tables.md#process-instances">process tables</see>. The record may change or be deleted
/// later, so inspection cannot read them from it afterwards.
/// </summary>
internal static class ProcessResults
{
    /// <summary>
    /// The subject fields that the steps of <paramref name="process"/> write, as an object by declared name, in the entity's field
    /// order, each with its value in <paramref name="stored"/>. These are the keys of each <c>updateRecord</c> <c>set</c> and the
    /// fields each connector or package operation's <c>output</c> writes. A field that is sensitive in <paramref name="pinned"/>
    /// holds <see langword="null"/>, never its value, so no plaintext copy of an encrypted field exists. It is an empty object
    /// without a subject, which writes no record.
    /// </summary>
    public static JsonObject Values(ApplicationModel pinned, ProcessModel process, EntityModel? entity, Record? stored)
    {
        var result = new JsonObject();
        if (entity is null || stored is null)
        {
            return result;
        }

        var written = process.Steps
            .SelectMany(step => step switch
            {
                OperationStepModel operation => operation.Set.Select(assignment => assignment.Field),
                CallConnectorStepModel call => call.Output.Select(output => output.Field),
                PackageOperationStepModel package => package.Output.Select(output => output.Field),
                _ => [],
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in entity.Fields.Where(candidate => written.Contains(candidate.Name)))
        {
            result[field.Name] = IsSensitive(pinned, field) ? null : stored.Values.GetValueOrDefault(field.Name)?.DeepClone();
        }

        return result;
    }

    // A child collection counts when any field of its child entity is sensitive, as in inspection.
    internal static bool IsSensitive(ApplicationModel pinned, FieldModel field) =>
        field.Sensitive
        || (field.Type == FieldType.ChildCollection
            && field.Target is { } target
            && pinned.FindEntity(target.Id) is { } child
            && child.Fields.Any(candidate => candidate.Sensitive));
}
