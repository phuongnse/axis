using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Policy;

namespace Axis.Processes.Records;

/// <summary>
/// The fields of an entity a user may read through the record API, and the reference labels they
/// may see. <see cref="Readable"/> is the union of the <c>fields</c> of the user's <c>read</c>
/// grants on the entity, or null when a grant covers every field. A reference label shows only
/// when the user may read the reference field and the target entity's display field. The target's
/// record filters do not hide it. A child row's label follows the same rule: the user may read the
/// owner's child collection field and the target's display field.
/// </summary>
public sealed class RecordFieldAccess
{
    private readonly HashSet<string> _labelled;
    private readonly Dictionary<string, HashSet<string>> _rowLabelled;
    private readonly HashSet<string> _sets;
    private readonly HashSet<string> _setLabelled;

    private RecordFieldAccess(
        IReadOnlySet<string>? readable,
        HashSet<string> labelled,
        Dictionary<string, HashSet<string>> rowLabelled,
        HashSet<string> sets,
        HashSet<string> setLabelled)
    {
        Readable = readable;
        _labelled = labelled;
        _rowLabelled = rowLabelled;
        _sets = sets;
        _setLabelled = setLabelled;
    }

    /// <summary>The declared names of the fields the user may read, or null for every field.</summary>
    public IReadOnlySet<string>? Readable { get; }

    /// <summary>What <paramref name="roles"/> may read of the records of <paramref name="model"/>.</summary>
    public static RecordFieldAccess Read(ApplicationModel application, IEnumerable<string> roles, EntityModel model)
    {
        var held = roles.ToList();
        var readable = PolicyEvaluator.Fields(application, held, model, PolicyAction.Read);
        var labelled = new HashSet<string>(StringComparer.Ordinal);
        var rowLabelled = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var sets = new HashSet<string>(StringComparer.Ordinal);
        var setLabelled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in model.Fields)
        {
            if (field.Type == FieldType.References)
            {
                sets.Add(field.Name);
                if (CanRead(readable, field.Name) && CanReadLabel(application, held, field))
                {
                    setLabelled.Add(field.Name);
                }
            }
            else if (field.Type == FieldType.ChildCollection)
            {
                if (CanRead(readable, field.Name) && application.FindEntity(field.Target!.Id) is { } child
                    && ReferenceLabelled(application, held, child) is { Count: > 0 } childLabelled)
                {
                    rowLabelled[field.Name] = childLabelled;
                }
            }
            else if (field.Type == FieldType.Reference && CanRead(readable, field.Name)
                && CanReadLabel(application, held, field))
            {
                labelled.Add(field.Name);
            }
        }

        return new RecordFieldAccess(readable, labelled, rowLabelled, sets, setLabelled);
    }

    /// <summary>The reference fields of <paramref name="child"/> whose label the user may read.</summary>
    private static HashSet<string> ReferenceLabelled(ApplicationModel application, List<string> held, EntityModel child) =>
        child.Fields
            .Where(field => field.Type == FieldType.Reference && CanReadLabel(application, held, field))
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static bool CanReadLabel(ApplicationModel application, List<string> held, FieldModel reference) =>
        application.FindEntity(reference.Target!.Id) is { } target
        && CanRead(PolicyEvaluator.Fields(application, held, target, PolicyAction.Read), reference.TargetDisplayField!);

    /// <summary>
    /// Whether <paramref name="roles"/> hold a <c>create</c> or an <c>update</c> grant on <paramref name="model"/>
    /// whose <c>fields</c> cover <paramref name="field"/>. Record filters are not read, because an upload has no record yet.
    /// </summary>
    public static bool CanWrite(ApplicationModel application, IEnumerable<string> roles, EntityModel model, string field)
    {
        var held = roles.ToList();
        foreach (var action in new[] { PolicyAction.Create, PolicyAction.Update })
        {
            if (!PolicyEvaluator.Access(application, held, model, action).Granted)
            {
                continue;
            }

            var covered = PolicyEvaluator.Fields(application, held, model, action);
            if (covered is null || covered.Contains(field))
            {
                return true;
            }
        }

        return false;
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
                .Select(label => (label.Key, Value: VisibleLabel(label.Key, label.Value)))
                .Where(label => label.Value is not null)
                .ToDictionary(label => label.Key, label => label.Value!, StringComparer.Ordinal),
        };

    /// <summary>
    /// A string label when the user may see it. A collection's array keeps each row's object with
    /// only the labels the user may see. A references field's array of labels is kept whole or left
    /// out. Null when there is nothing to show.
    /// </summary>
    private JsonNode? VisibleLabel(string name, JsonNode label)
    {
        if (_sets.Contains(name))
        {
            return _setLabelled.Contains(name) ? label : null;
        }

        if (label is not JsonArray rows)
        {
            return _labelled.Contains(name) ? label : null;
        }

        if (!_rowLabelled.TryGetValue(name, out var allowed))
        {
            return null;
        }

        var visible = new JsonArray();
        foreach (var row in rows)
        {
            var kept = new JsonObject();
            foreach (var (key, value) in row?.AsObject() ?? [])
            {
                if (allowed.Contains(key) && value is not null)
                {
                    kept[key] = value.DeepClone();
                }
            }

            visible.Add(kept);
        }

        return visible;
    }

    /// <summary>
    /// An error at <c>/values/&lt;field&gt;</c> for each field and child collection
    /// <paramref name="input"/> sets that <paramref name="writable"/> does not hold, or null when
    /// there is none. A child collection is one field, so its rows are not checked one by one, and a
    /// references field is one field, so its ids are not.
    /// </summary>
    public static SortedDictionary<string, string[]>? Unwritable(RecordInput input, IReadOnlySet<string>? writable)
    {
        if (writable is null)
        {
            return null;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var name in input.Values.Select(value => value.Field.Name)
            .Concat(input.Rows.Select(rows => rows.Collection.Name))
            .Concat(input.Links.Select(links => links.Field.Name)))
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
