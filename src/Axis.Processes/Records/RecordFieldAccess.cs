using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Policy;

namespace Axis.Processes.Records;

/// <summary>
/// The fields of an entity a user may read through the record API, and the reference labels they
/// may see. <see cref="Readable"/> is the union of the <c>fields</c> of the user's <c>read</c>
/// grants on the entity, or null when a grant covers every field. A reference label shows only
/// when the user may read the reference field and the target entity's display field. The target's
/// record filters do not hide it.
/// </summary>
public sealed class RecordFieldAccess
{
    private readonly HashSet<string> _labelled;

    private RecordFieldAccess(IReadOnlySet<string>? readable, HashSet<string> labelled)
    {
        Readable = readable;
        _labelled = labelled;
    }

    /// <summary>The declared names of the fields the user may read, or null for every field.</summary>
    public IReadOnlySet<string>? Readable { get; }

    /// <summary>What <paramref name="roles"/> may read of the records of <paramref name="model"/>.</summary>
    public static RecordFieldAccess Read(ApplicationModel application, IEnumerable<string> roles, EntityModel model)
    {
        var held = roles.ToList();
        var readable = PolicyEvaluator.Fields(application, held, model, PolicyAction.Read);
        var labelled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in model.Fields)
        {
            if (field.Type != FieldType.Reference || !CanRead(readable, field.Name)
                || application.FindEntity(field.Target!.Id) is not { } target)
            {
                continue;
            }

            if (CanRead(PolicyEvaluator.Fields(application, held, target, PolicyAction.Read), field.TargetDisplayField!))
            {
                labelled.Add(field.Name);
            }
        }

        return new RecordFieldAccess(readable, labelled);
    }

    /// <summary>Whether the user may read the field named <paramref name="name"/>.</summary>
    public bool CanRead(string name) => CanRead(Readable, name);

    /// <summary>
    /// <paramref name="record"/> with only the values the user may read, in their order, and only
    /// the labels they may see.
    /// </summary>
    public Record Visible(Record record) =>
        record with
        {
            Values = record.Values
                .Where(value => CanRead(value.Key))
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal),
            Labels = record.Labels
                .Where(label => _labelled.Contains(label.Key))
                .ToDictionary(label => label.Key, label => label.Value, StringComparer.Ordinal),
        };

    /// <summary>
    /// An error at <c>/values/&lt;field&gt;</c> for each field and child collection
    /// <paramref name="input"/> sets that <paramref name="writable"/> does not hold, or null when
    /// there is none. A child collection is one field, so its rows are not checked one by one.
    /// </summary>
    public static SortedDictionary<string, string[]>? Unwritable(RecordInput input, IReadOnlySet<string>? writable)
    {
        if (writable is null)
        {
            return null;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var name in input.Values.Select(value => value.Field.Name).Concat(input.Rows.Select(rows => rows.Collection.Name)))
        {
            if (!writable.Contains(name))
            {
                errors[$"/values/{name}"] = ["You have no grant to write this field."];
            }
        }

        return errors.Count > 0 ? errors : null;
    }

    private static bool CanRead(IReadOnlySet<string>? readable, string name) => readable is null || readable.Contains(name);
}
