using System.Globalization;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// The values and rows of a write with every computed field set, or the errors that refuse the
/// write. <see cref="Errors"/> is keyed by JSON Pointer in ordinal key order, and is null when
/// every computed field got a value.
/// </summary>
public sealed record RecordComputeResult(
    IReadOnlyList<RecordValue> Values,
    IReadOnlyList<RecordRows> Rows,
    SortedDictionary<string, string[]>? Errors);

/// <summary>
/// Computes the computed fields of a record as it will be stored, and of each row a body sends.
/// An expression reads the record's fields that are not computed. The rows are computed before
/// the owner, so the owner's aggregates read the rows' computed values. An aggregate reads the
/// body's rows of a collection, or the stored rows when an update leaves the collection out. A
/// run-time error, or a value that does not fit its column, is an error at the computed field's
/// pointer, <c>/values/&lt;field&gt;</c> or <c>/values/&lt;collection&gt;/&lt;index&gt;/&lt;field&gt;</c>.
/// A value is never rounded. Rows an update leaves out are not computed again.
/// </summary>
public static class RecordComputer
{
    /// <summary>Computes a create: every field the body leaves out is <c>null</c>.</summary>
    public static RecordComputeResult ComputeCreate(
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);

        return Compute(application, entity, null, values, rows);
    }

    /// <summary>
    /// Computes an update: the record is the <paramref name="stored"/> values with the changes from
    /// the body on top, and every computed field of the owner is set, even when the body is empty.
    /// <paramref name="stored"/> also holds the stored rows that the owner's aggregates read for a
    /// collection the body leaves out.
    /// </summary>
    /// <exception cref="ArgumentException">The entity has computed fields and <paramref name="stored"/> is null.</exception>
    public static RecordComputeResult ComputeUpdate(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);
        if (stored is null && entity.HasComputedFields)
        {
            throw new ArgumentException("An update of an entity with computed fields needs the stored record.", nameof(stored));
        }

        return Compute(application, entity, stored, values, rows);
    }

    private static RecordComputeResult Compute(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        if (!entity.HasComputedFields && rows.All(collection => !collection.Child.HasComputedFields))
        {
            return new RecordComputeResult(values, rows, null);
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        var computedRows = new List<RecordRows>();
        foreach (var collection in rows)
        {
            if (!collection.Child.HasComputedFields)
            {
                computedRows.Add(collection);
                continue;
            }

            var computed = new List<IReadOnlyList<RecordValue>>();
            for (var index = 0; index < collection.Rows.Count; index++)
            {
                var row = collection.Rows[index];
                var record = NewRecord(collection.Child);
                foreach (var value in row.Where(value => !value.Field.IsComputed))
                {
                    record[value.Field.Name] = (RecordClrValues.FromInput(value, out var exact), exact);
                }

                var results = Run(collection.Child, record, $"/values/{collection.Collection.Name}/{index}", errors);
                computed.Add([.. row.Select(value => value.Field.IsComputed ? results.GetValueOrDefault(value.Field.Name) ?? value : value)]);
            }

            computedRows.Add(collection with { Rows = computed });
        }

        var computedValues = values;
        if (entity.HasComputedFields)
        {
            var record = NewRecord(entity);
            if (stored is not null)
            {
                foreach (var field in RecordQueries.Columns(entity).Where(field => !field.IsComputed))
                {
                    var exact = RecordClrValues.TryFromStored(field, stored.Values.GetValueOrDefault(field.Name), out var value);
                    record[field.Name] = (value, exact);
                }
            }

            foreach (var value in values)
            {
                record[value.Field.Name] = (RecordClrValues.FromInput(value, out var exact), exact);
            }

            // A row value that cannot be held exactly leaves every computed field of the owner out.
            var results = RecordCollections.TryAdd(application, entity, stored, computedRows, record, errors)
                ? Run(entity, record, "/values", errors)
                : [];

            // The body never holds a computed field, so each column has one value: the body's or the computed one.
            var byName = values.ToDictionary(value => value.Field.Name, StringComparer.Ordinal);
            computedValues = RecordQueries.Columns(entity)
                .Select(field => byName.GetValueOrDefault(field.Name) ?? results.GetValueOrDefault(field.Name))
                .OfType<RecordValue>()
                .ToList();
        }

        return new RecordComputeResult(computedValues, computedRows, errors.Count == 0 ? null : errors);
    }

    /// <summary>Starts a record with every column field that is not computed set to null.</summary>
    private static Dictionary<string, (object? Value, bool Exact)> NewRecord(EntityModel entity)
    {
        var record = new Dictionary<string, (object? Value, bool Exact)>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in RecordQueries.Columns(entity).Where(field => !field.IsComputed))
        {
            record[field.Name] = (null, true);
        }

        return record;
    }

    /// <summary>
    /// Computes the computed fields of one record or row, keyed by the field's declared name. A
    /// value that cannot be held exactly is an error at its own pointer, and then nothing of that
    /// record is computed, because no expression could be evaluated without rounding.
    /// </summary>
    private static Dictionary<string, RecordValue> Run(
        EntityModel entity,
        Dictionary<string, (object? Value, bool Exact)> record,
        string prefix,
        SortedDictionary<string, string[]> errors)
    {
        var results = new Dictionary<string, RecordValue>(StringComparer.Ordinal);
        var inexact = record.Where(pair => !pair.Value.Exact).Select(pair => pair.Key).ToList();
        if (inexact.Count > 0)
        {
            foreach (var name in inexact)
            {
                entity.TryGetField(name, out var field);
                errors.TryAdd($"{prefix}/{field!.Name}", [Messages.NotEvaluable]);
            }

            return results;
        }

        var values = new ExpressionValues(record.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        foreach (var field in entity.Fields.Where(field => field.IsComputed))
        {
            var result = ExpressionInterpreter.Evaluate(field.Computed!.Syntax, field.Computed.Check, values);
            RecordValue? value = null;
            var message = result.Succeeded ? ToRecordValue(field, result.Value, out value) : Messages.NotComputable;
            if (message is not null)
            {
                errors.TryAdd($"{prefix}/{field.Name}", [message]);
            }
            else
            {
                results[field.Name] = value!;
            }
        }

        return results;
    }

    /// <summary>
    /// Converts a computed value to the value that is bound, as <see cref="RecordInputParser"/>
    /// reads it from a body. Returns the error message when the value does not fit the column, or
    /// null when it does.
    /// </summary>
    private static string? ToRecordValue(FieldModel field, object? computed, out RecordValue? value)
    {
        value = null;
        if (computed is null)
        {
            value = new RecordValue(field, null);
            return null;
        }

        switch (field.Type)
        {
            case FieldType.Decimal:
                // An integer result widens to a decimal, and the digits are checked like a body's.
                var text = (computed is long integer ? integer : (decimal)computed).ToString(CultureInfo.InvariantCulture);
                if (RecordInputParser.CheckDecimalText(field, text) is { } digits)
                {
                    return digits;
                }

                value = new RecordValue(field, text);
                return null;
            case FieldType.Text:
                var characters = (string)computed;
                if (characters.Contains('\0', StringComparison.Ordinal))
                {
                    return Messages.NullCharacter;
                }

                // A character outside the BMP is one code point, as PostgreSQL counts it.
                if (field.MaxLength is { } maxLength && characters.EnumerateRunes().Count() > maxLength)
                {
                    return Messages.MaxLength(maxLength);
                }

                value = new RecordValue(field, characters);
                return null;
            case FieldType.Enum:
                if (field.Values?.Contains((string)computed, StringComparer.Ordinal) != true)
                {
                    return Messages.Enum;
                }

                value = new RecordValue(field, computed);
                return null;
            case FieldType.Integer or FieldType.Boolean or FieldType.Date or FieldType.DateTime:
                value = new RecordValue(field, computed);
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field.Type, "A field of this type cannot be computed.");
        }
    }
}
