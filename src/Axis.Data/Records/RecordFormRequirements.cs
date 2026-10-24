using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;

namespace Axis.Data.Records;

/// <summary>
/// Checks the conditions of a form on a record as it will be stored. A field is required when it is
/// visible, its <c>requiredWhen</c> is <c>true</c> and its value is <c>null</c>. A field is visible
/// when its own <c>visibleWhen</c> and its section's are each absent or <c>true</c>. A condition
/// that is <c>null</c> or stops with a run-time error counts as false, so such a field is hidden or
/// not required. A child collection and a numbered field are never checked for a required value,
/// because a sequence number is assigned in storage after this check. A failure is keyed
/// <c>/values/&lt;field&gt;</c> with the message <c>Required.</c>. A field the caller says it sets
/// is also refused with <c>Cannot be set.</c> when it is hidden or its <c>readOnlyWhen</c> is
/// <c>true</c>, a child collection included. <c>now()</c> gives the time the caller passes, which is
/// the start time of the transaction that writes the record.
/// </summary>
public static class RecordFormRequirements
{
    /// <summary>
    /// Checks the record: the <paramref name="stored"/> values, or <c>null</c> for a create, with
    /// the <paramref name="values"/> on top. Returns the failures in ordinal key order, or
    /// <see langword="null"/> when no required field is empty and no sent field is refused.
    /// <paramref name="sent"/> holds the declared names of the fields the caller sets. The record API
    /// passes none, because it never enforces <c>readOnlyWhen</c> or hidden fields.
    /// </summary>
    public static SortedDictionary<string, string[]>? Check(
        ApplicationModel application,
        EntityModel entity,
        FormModel form,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        DateTimeOffset? now,
        IReadOnlyCollection<string>? sent = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (!form.HasRequiredWhen && (sent is not { Count: > 0 } || !form.HasVisibleOrReadOnlyWhen))
        {
            return null;
        }

        var record = RecordValidator.BuildRecord(entity, stored, values);
        var rowsExact = RecordCollections.TryAdd(application, entity, stored, rows, record, errors);

        // A value that cannot be held exactly stops every condition, as it stops the validations.
        var inexact = record.Where(pair => !pair.Value.Exact).Select(pair => pair.Key).ToList();
        if (!rowsExact || inexact.Count > 0)
        {
            foreach (var name in inexact)
            {
                entity.TryGetField(name, out var field);
                errors.TryAdd($"/values/{field!.Name}", [RecordInputMessages.NotEvaluable]);
            }

            return errors.Count == 0 ? null : errors;
        }

        var expressionValues = new ExpressionValues(record.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        foreach (var section in form.Sections)
        {
            var sectionVisible = Holds(section.VisibleWhen, true, expressionValues, now);
            foreach (var formField in section.Fields)
            {
                if (!entity.TryGetField(formField.Field, out var field))
                {
                    continue;
                }

                var visible = sectionVisible && Holds(formField.VisibleWhen, true, expressionValues, now);
                if (sent is not null
                    && sent.Contains(field.Name, StringComparer.Ordinal)
                    && (!visible || Holds(formField.ReadOnlyWhen, false, expressionValues, now)))
                {
                    errors[$"/values/{field.Name}"] = [RecordInputMessages.Computed];
                    continue;
                }

                if (formField.RequiredWhen is null
                    || field.Type == FieldType.ChildCollection
                    || field.Sequence is not null
                    || record[field.Name].Value is not null
                    || !visible
                    || !Holds(formField.RequiredWhen, false, expressionValues, now))
                {
                    continue;
                }

                errors[$"/values/{field.Name}"] = [RecordInputMessages.Required];
            }
        }

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>
    /// Merges the form's failures with the validator's. Each key keeps one message, and the form's
    /// <c>Required.</c> wins over a validation message on the same key. Returns <see langword="null"/>
    /// when both are empty.
    /// </summary>
    public static SortedDictionary<string, string[]>? Merge(
        SortedDictionary<string, string[]>? formErrors,
        SortedDictionary<string, string[]>? validationErrors)
    {
        if (formErrors is null)
        {
            return validationErrors;
        }

        if (validationErrors is not null)
        {
            foreach (var (key, messages) in validationErrors)
            {
                formErrors.TryAdd(key, messages);
            }
        }

        return formErrors;
    }

    /// <summary>Whether <paramref name="condition"/> is <c>true</c>, or <paramref name="whenAbsent"/> when there is none.</summary>
    private static bool Holds(ExpressionModel? condition, bool whenAbsent, ExpressionValues values, DateTimeOffset? now)
    {
        if (condition is null)
        {
            return whenAbsent;
        }

        var result = ExpressionInterpreter.Evaluate(condition.Syntax, condition.Check, values, records: null, now: now);
        return result.Succeeded && result.Value is true;
    }
}
