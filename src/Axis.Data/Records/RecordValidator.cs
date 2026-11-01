using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;

namespace Axis.Data.Records;

/// <summary>
/// Runs an entity's validations on a record as it will be stored, and the child entity's
/// validations on each row a body sends. A validation fails when its expression is <c>false</c>,
/// <c>null</c> or stops with a run-time error. A failure is keyed <c>/values/&lt;field&gt;</c>, or
/// <c>/values/&lt;collection&gt;/&lt;index&gt;/&lt;field&gt;</c> for a row, and its message is the
/// validation's text key. When several validations fail on one key, the first in declaration
/// order is reported. The owner's aggregates read the same rows as <see cref="RecordComputer"/>:
/// the body's rows of a collection, or the stored rows when an update leaves the collection out.
/// An aggregate over a <c>references</c> field reads the target records in the
/// <see cref="RecordSets"/> the caller passes, and none without them.
/// Rows an update leaves out are not checked again as rows. <c>now()</c> gives the time the caller
/// passes, which is the start time of the transaction that writes the record.
/// </summary>
public static class RecordValidator
{
    /// <summary>
    /// Validates a create: every field the body leaves out is <c>null</c>. Returns the failures in
    /// ordinal key order, or <see langword="null"/> when every validation passes.
    /// </summary>
    public static SortedDictionary<string, string[]>? ValidateCreate(
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        DateTimeOffset? now = null,
        RecordSets? sets = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);

        return Validate(application, entity, null, values, rows, now, sets);
    }

    /// <summary>
    /// Validates an update: the record is the <paramref name="stored"/> values with the changes
    /// from the body on top. Returns the failures in ordinal key order, or <see langword="null"/>
    /// when every validation passes.
    /// </summary>
    /// <exception cref="ArgumentException">The entity has validations and <paramref name="stored"/> is null.</exception>
    public static SortedDictionary<string, string[]>? ValidateUpdate(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        DateTimeOffset? now = null,
        RecordSets? sets = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);
        if (stored is null && entity.Validations.Count > 0)
        {
            throw new ArgumentException("An update of an entity with validations needs the stored record.", nameof(stored));
        }

        return Validate(application, entity, stored, values, rows, now, sets);
    }

    /// <summary>
    /// Whether a lifecycle guard is <c>true</c> on the <paramref name="stored"/> record with
    /// <paramref name="values"/> and <paramref name="rows"/> on top. <c>previous(field)</c> reads the
    /// <paramref name="stored"/> record as it was before the write. <c>accountableUser()</c> and
    /// <c>decisionTask()</c> read <paramref name="accountability"/>, the values of the write. A <c>false</c>, a <c>null</c>, a
    /// run-time error and a value that cannot be held exactly, in the new record or the stored one,
    /// are all <see langword="false"/>.
    /// </summary>
    internal static bool GuardHolds(
        ApplicationModel application,
        EntityModel entity,
        Record stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        ExpressionModel guard,
        DateTimeOffset now,
        RecordSets sets,
        ExpressionAccountability accountability)
    {
        var record = BuildRecord(entity, stored, values);
        var before = BuildRecord(entity, stored, []);
        if (!RecordCollections.TryAdd(application, entity, stored, rows, record, new SortedDictionary<string, string[]>(StringComparer.Ordinal), sets)
            || record.Values.Any(pair => !pair.Exact)
            || before.Values.Any(pair => !pair.Exact))
        {
            return false;
        }

        var result = ExpressionInterpreter.Evaluate(
            guard.Syntax,
            guard.Check,
            new ExpressionValues(record.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value))),
            records: null,
            now: now,
            timeZone: application.TimeZone,
            previous: new ExpressionValues(before.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value))),
            accountability: accountability);
        return result.Succeeded && result.Value is true;
    }

    /// <summary>
    /// Runs the core validations of the entity's module contract on the record as it will be stored: the
    /// <paramref name="stored"/> record, or none on a create, with <paramref name="values"/> and
    /// <paramref name="rows"/> on top. <c>previous(field)</c> reads the stored record as it was before the
    /// write, and is <c>null</c> for every field on a create. <c>accountableUser()</c> and
    /// <c>decisionTask()</c> read <paramref name="accountability"/>. A validation fails when its expression is
    /// <c>false</c>, <c>null</c> or stops with a run-time error. A failure is keyed <c>/values/&lt;field&gt;</c>
    /// and the first in declaration order wins a key. Returns the failures in ordinal key order, or
    /// <see langword="null"/> when every validation passes. <paramref name="point"/> is the named point of the
    /// first failed validation, or <see langword="null"/> when a value could not be evaluated.
    /// </summary>
    internal static SortedDictionary<string, string[]>? ValidateCore(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        DateTimeOffset now,
        RecordSets sets,
        ExpressionAccountability accountability,
        out string? point)
    {
        point = null;
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (entity.Contract is not { Validations.Count: > 0 } contract)
        {
            return null;
        }

        var record = BuildRecord(entity, stored, values);
        var before = BuildRecord(entity, stored, []);
        if (!RecordCollections.TryAdd(application, entity, stored, rows, record, errors, sets))
        {
            return errors;
        }

        var inexact = record.Where(pair => !pair.Value.Exact).Select(pair => pair.Key).ToList();
        if (inexact.Count > 0)
        {
            foreach (var name in inexact)
            {
                entity.TryGetField(name, out var field);
                errors.TryAdd($"/values/{field!.Name}", [RecordInputMessages.NotEvaluable]);
            }

            return errors;
        }

        // A stored value that cannot be held exactly leaves previous() without a record to read, so every validation fails.
        var storedExact = before.Values.All(pair => pair.Exact);
        var current = new ExpressionValues(record.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        var previous = new ExpressionValues(before.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        string? firstPoint = null;
        foreach (var validation in contract.Validations)
        {
            var holds = storedExact
                && ExpressionInterpreter.Evaluate(
                    validation.Expression.Syntax,
                    validation.Expression.Check,
                    current,
                    records: null,
                    now: now,
                    timeZone: application.TimeZone,
                    previous: previous,
                    accountability: accountability) is { Succeeded: true, Value: true };
            if (!holds)
            {
                errors.TryAdd($"/values/{validation.Field}", [validation.Message.TextKey]);
                firstPoint ??= $"{entity.Name[(entity.Name.LastIndexOf('/') + 1)..]}/validations/{validation.Name}";
            }
        }

        point = firstPoint;
        return errors.Count == 0 ? null : errors;
    }

    private static SortedDictionary<string, string[]>? Validate(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows,
        DateTimeOffset? now,
        RecordSets? sets)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        if (entity.Validations.Count > 0)
        {
            var record = BuildRecord(entity, stored, values);

            // A row value that cannot be held exactly stops every validation of the owner.
            if (RecordCollections.TryAdd(application, entity, stored, rows, record, errors, sets))
            {
                Run(entity, record, "/values", errors, now, application.TimeZone);
            }
        }

        foreach (var collection in rows)
        {
            if (collection.Child.Validations.Count == 0)
            {
                continue;
            }

            for (var index = 0; index < collection.Rows.Count; index++)
            {
                var row = new Dictionary<string, (object? Value, bool Exact)>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in collection.Rows[index])
                {
                    row[value.Field.Name] = (RecordClrValues.FromInput(value, out var exact), exact);
                }

                Run(collection.Child, row, $"/values/{collection.Collection.Name}/{index}", errors, now, application.TimeZone);
            }
        }

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>
    /// The record as the interpreter reads it, keyed by field name: every column field starts as
    /// null, then takes its stored value and then the value from <paramref name="values"/>. Child
    /// collections are added with <see cref="RecordCollections.TryAdd"/>.
    /// </summary>
    internal static Dictionary<string, (object? Value, bool Exact)> BuildRecord(
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values)
    {
        var record = new Dictionary<string, (object? Value, bool Exact)>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in entity.Fields.Where(field => field.HasColumn))
        {
            record[field.Name] = (null, true);
        }

        if (stored is not null)
        {
            foreach (var field in entity.Fields.Where(field => field.HasColumn))
            {
                var exact = RecordClrValues.TryFromStored(field, stored.Values.GetValueOrDefault(field.Name), out var value);
                record[field.Name] = (value, exact);
            }
        }

        foreach (var value in values)
        {
            record[value.Field.Name] = (RecordClrValues.FromInput(value, out var exact), exact);
        }

        return record;
    }

    /// <summary>
    /// Runs the validations of one record or row. A value that cannot be held exactly, such as a
    /// decimal with more digits than the interpreter holds, is an error at its own pointer, and
    /// then no validation of that record runs, because none could be evaluated without rounding.
    /// </summary>
    private static void Run(
        EntityModel entity,
        Dictionary<string, (object? Value, bool Exact)> record,
        string prefix,
        SortedDictionary<string, string[]> errors,
        DateTimeOffset? now,
        TimeZoneInfo timeZone)
    {
        var inexact = record.Where(pair => !pair.Value.Exact).Select(pair => pair.Key).ToList();
        if (inexact.Count > 0)
        {
            foreach (var name in inexact)
            {
                entity.TryGetField(name, out var field);
                errors.TryAdd($"{prefix}/{field!.Name}", [RecordInputMessages.NotEvaluable]);
            }

            return;
        }

        var values = new ExpressionValues(record.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        foreach (var validation in entity.Validations)
        {
            var result = ExpressionInterpreter.Evaluate(validation.Syntax, validation.Check, values, records: null, now: now, timeZone: timeZone);
            if (!result.Succeeded || result.Value is not true)
            {
                errors.TryAdd($"{prefix}/{validation.Field}", [validation.Message.TextKey]);
            }
        }
    }
}
