using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Forms;

/// <summary>What a form shows for a draft: its sections, its fields that the user may read, and their messages.</summary>
/// <param name="Sections">One entry for each named section that holds a field the user may read.</param>
/// <param name="Fields">One entry for each form field the user may read, in form order, by declared name.</param>
/// <param name="Errors">The messages of touched, readable fields, keyed by RFC 6901 JSON Pointer in ordinal key order.</param>
/// <param name="Defaults">
/// For a new record, the default of each form field that the user may read and the draft leaves out,
/// in form order and in the record API's value format. Always empty for a stored record.
/// </param>
public sealed record FormState(
    IReadOnlyList<FormSectionState> Sections,
    IReadOnlyDictionary<string, FormFieldState> Fields,
    SortedDictionary<string, string[]> Errors,
    IReadOnlyDictionary<string, JsonNode?> Defaults);

/// <summary>Whether a named section is visible.</summary>
public sealed record FormSectionState(string Name, bool Visible);

/// <summary>
/// A field's state. <see cref="Required"/> holds for a visible field that the entity requires or
/// whose <c>requiredWhen</c> is true. <see cref="ReadOnly"/> covers the form's rules only, not the
/// user's field write grants.
/// </summary>
public sealed record FormFieldState(bool Visible, bool Required, bool ReadOnly);

/// <summary>
/// Evaluates a form's conditions and the entity's validations on a draft record, which writes
/// nothing. The draft is the stored record with the draft values on top, with the computed fields
/// calculated, as a write would store it. For a new record each field the draft leaves out first takes
/// its default, as a create does, and the answer returns those defaults. A field whose value does not
/// parse gets no default, and a default that cannot be computed is left out and shows its message. A condition counts as true only when it gives
/// <c>true</c>; <c>null</c> and a run-time error count as false. When a value of the draft cannot
/// be held exactly, no condition could be evaluated without rounding, so every condition counts as
/// false. Unique and reference-existence checks are left to a save.
/// </summary>
public static class FormStateEvaluator
{
    private const string ValuesPrefix = "/values/";

    /// <summary>
    /// Evaluates <paramref name="form"/> of <paramref name="entity"/> on <paramref name="draft"/>.
    /// <paramref name="stored"/> is null for a new record. <paramref name="readable"/> holds the
    /// declared names of the fields the user may read, or is null when the user may read every field.
    /// <paramref name="now"/> is what <c>now()</c> gives.
    /// </summary>
    public static FormState Evaluate(
        ApplicationModel application,
        FormModel form,
        EntityModel entity,
        Record? stored,
        RecordDraftResult draft,
        IReadOnlySet<string> touched,
        IReadOnlySet<string>? readable,
        string actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(touched);

        // The parse errors come first, then the compute errors and then the validation failures.
        var errors = new SortedDictionary<string, string[]>(draft.Errors, StringComparer.Ordinal);
        var (values, rows, filled) = Complete(application, entity, stored, draft, actorId, now, errors);

        var record = RecordValidator.BuildRecord(entity, stored, values);
        var conditions = RecordCollections.TryAdd(application, entity, stored, rows, record, errors) && record.Values.All(pair => pair.Exact)
            ? new ExpressionValues(record.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)))
            : null;

        var sections = new List<FormSectionState>();
        var fields = new Dictionary<string, FormFieldState>(StringComparer.Ordinal);
        foreach (var section in form.Sections)
        {
            var sectionVisible = section.VisibleWhen is null || Holds(section.VisibleWhen, conditions, now, application.TimeZone);
            var anyReadable = false;
            foreach (var entry in section.Fields)
            {
                if (!entity.TryGetField(entry.Field, out var field))
                {
                    continue;
                }

                var visible = sectionVisible && (entry.VisibleWhen is null || Holds(entry.VisibleWhen, conditions, now, application.TimeZone));
                var required = visible && (field.Required || (entry.RequiredWhen is not null && Holds(entry.RequiredWhen, conditions, now, application.TimeZone)));
                var readOnly = entry.ReadOnly
                    || field.IsComputed
                    || field.Sequence is not null
                    || field.Creator
                    || (entry.ReadOnlyWhen is not null && Holds(entry.ReadOnlyWhen, conditions, now, application.TimeZone));
                if (required && field.HasColumn && record[field.Name].Value is null)
                {
                    errors.TryAdd(ValuesPrefix + field.Name, [Messages.Required]);
                }

                if (readable is null || readable.Contains(field.Name))
                {
                    anyReadable = true;
                    fields[field.Name] = new FormFieldState(visible, required, readOnly);
                }
            }

            if (section.Name is not null && anyReadable)
            {
                sections.Add(new FormSectionState(section.Name, sectionVisible));
            }
        }

        var defaults = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var name in fields.Keys)
        {
            if (filled.TryGetValue(name, out var value))
            {
                defaults[name] = RecordValueJson.ToJson(value);
            }
        }

        // Only the keys of fields that the user touched and may read are answered.
        var answered = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in errors)
        {
            if (FieldOf(key) is { } name && fields.ContainsKey(name) && touched.Contains(name))
            {
                answered[key] = messages;
            }
        }

        return new FormState(sections, fields, answered, defaults);
    }

    /// <summary>
    /// Sets the defaults of a new record and the creator, calculates the computed fields and runs the
    /// entity's validations, adding each error to <paramref name="errors"/> unless its key already has
    /// one. Returns the values and rows as a write would store them, and the values that came from a
    /// default by field name.
    /// </summary>
    private static (IReadOnlyList<RecordValue> Values, IReadOnlyList<RecordRows> Rows, IReadOnlyDictionary<string, RecordValue> Defaults) Complete(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        RecordDraftResult draft,
        string actorId,
        DateTimeOffset now,
        SortedDictionary<string, string[]> errors)
    {
        // The update overloads throw for a null stored record, so a new record takes the create ones.
        var input = draft.Input;
        IReadOnlyList<RecordValue> values = input.Values;
        var filled = new Dictionary<string, RecordValue>(StringComparer.Ordinal);
        if (stored is null)
        {
            // A field with a parse error is not left out: the user sent something, so it gets no default.
            var defaults = RecordDefaults.Apply(entity, input.Values, now);
            var given = input.Values.Select(value => value.Field.Name).ToHashSet(StringComparer.Ordinal);
            var sent = new HashSet<string>(given, StringComparer.Ordinal);
            sent.UnionWith(draft.Errors.Keys.Select(FieldOf).OfType<string>());
            foreach (var value in defaults.Values)
            {
                if (!sent.Contains(value.Field.Name))
                {
                    filled[value.Field.Name] = value;
                }
            }

            Merge(errors, defaults.Errors);
            values = RecordCreator.WithCreator(entity, [.. defaults.Values.Where(value => given.Contains(value.Field.Name) || filled.ContainsKey(value.Field.Name))], actorId);
        }

        var computed = stored is null
            ? RecordComputer.ComputeCreate(application, entity, values, input.Rows)
            : RecordComputer.ComputeUpdate(application, entity, stored, values, input.Rows);
        Merge(errors, computed.Errors);

        var failures = stored is null
            ? RecordValidator.ValidateCreate(application, entity, computed.Values, computed.Rows, now)
            : RecordValidator.ValidateUpdate(application, entity, stored, computed.Values, computed.Rows, now);
        Merge(errors, failures);
        return (computed.Values, computed.Rows, filled);
    }

    private static void Merge(SortedDictionary<string, string[]> errors, SortedDictionary<string, string[]>? more)
    {
        if (more is null)
        {
            return;
        }

        foreach (var (key, messages) in more)
        {
            errors.TryAdd(key, messages);
        }
    }

    /// <summary>True only when <paramref name="condition"/> gives <c>true</c>; false when it cannot be evaluated.</summary>
    private static bool Holds(ExpressionModel condition, ExpressionValues? values, DateTimeOffset now, TimeZoneInfo timeZone) =>
        values is not null
        && ExpressionInterpreter.Evaluate(condition.Syntax, condition.Check, values, records: null, now: now, timeZone: timeZone) is { Succeeded: true, Value: true };

    /// <summary>The field a key <c>/values/&lt;field&gt;[/…]</c> belongs to, or null for any other key.</summary>
    private static string? FieldOf(string key)
    {
        if (!key.StartsWith(ValuesPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = key.IndexOf('/', ValuesPrefix.Length);
        return end < 0 ? key[ValuesPrefix.Length..] : key[ValuesPrefix.Length..end];
    }
}
