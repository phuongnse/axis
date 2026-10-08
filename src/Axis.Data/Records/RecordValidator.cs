using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;

namespace Axis.Data.Records;

/// <summary>
/// Runs an entity's validations on a record as it will be stored, and the child entity's
/// validations on each row a body sends. A validation fails when its expression is <c>false</c>,
/// <c>null</c> or stops with a run-time error. A failure is keyed <c>/values/&lt;field&gt;</c>, or
/// <c>/values/&lt;collection&gt;/&lt;index&gt;/&lt;field&gt;</c> for a row, and its message is the
/// validation's text key. When several validations fail on one key, the first in declaration
/// order is reported. Rows an update leaves out are not checked again.
/// </summary>
public static class RecordValidator
{
    /// <summary>
    /// Validates a create: every field the body leaves out is <c>null</c>. Returns the failures in
    /// ordinal key order, or <see langword="null"/> when every validation passes.
    /// </summary>
    public static SortedDictionary<string, string[]>? ValidateCreate(
        EntityModel entity,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);

        return Validate(entity, [], values, rows);
    }

    /// <summary>
    /// Validates an update: the record is the <paramref name="stored"/> values with the changes
    /// from the body on top. Returns the failures in ordinal key order, or <see langword="null"/>
    /// when every validation passes.
    /// </summary>
    /// <exception cref="ArgumentException">The entity has validations and <paramref name="stored"/> is null.</exception>
    public static SortedDictionary<string, string[]>? ValidateUpdate(
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rows);
        if (stored is null && entity.Validations.Count > 0)
        {
            throw new ArgumentException("An update of an entity with validations needs the stored record.", nameof(stored));
        }

        var storedValues = new List<(FieldModel Field, object? Value, bool Exact)>();
        if (stored is not null && entity.Validations.Count > 0)
        {
            foreach (var field in entity.Fields.Where(field => field.HasColumn))
            {
                var exact = TryFromStored(field, stored.Values.GetValueOrDefault(field.Name), out var value);
                storedValues.Add((field, value, exact));
            }
        }

        return Validate(entity, storedValues, values, rows);
    }

    private static SortedDictionary<string, string[]>? Validate(
        EntityModel entity,
        IReadOnlyList<(FieldModel Field, object? Value, bool Exact)> stored,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        if (entity.Validations.Count > 0)
        {
            // Every column field starts as null, then takes its stored value and then the body's.
            var record = new Dictionary<string, (object? Value, bool Exact)>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in entity.Fields.Where(field => field.HasColumn))
            {
                record[field.Name] = (null, true);
            }

            foreach (var (field, value, exact) in stored)
            {
                record[field.Name] = (value, exact);
            }

            foreach (var value in values)
            {
                record[value.Field.Name] = (FromInput(value, out var exact), exact);
            }

            Run(entity, record, "/values", errors);
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
                    row[value.Field.Name] = (FromInput(value, out var exact), exact);
                }

                Run(collection.Child, row, $"/values/{collection.Collection.Name}/{index}", errors);
            }
        }

        return errors.Count == 0 ? null : errors;
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
        SortedDictionary<string, string[]> errors)
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
            var result = ExpressionInterpreter.Evaluate(validation.Syntax, validation.Check, values);
            if (!result.Succeeded || result.Value is not true)
            {
                errors.TryAdd($"{prefix}/{validation.Field}", [validation.Message.TextKey]);
            }
        }
    }

    /// <summary>
    /// Converts a parsed body value to the CLR type the interpreter reads. Every type but decimal
    /// already has it; a decimal is plain-notation text. <paramref name="exact"/> is false for a
    /// decimal that <see cref="decimal"/> cannot hold without rounding.
    /// </summary>
    private static object? FromInput(RecordValue value, out bool exact)
    {
        exact = true;
        if (value.Value is null || value.Field.Type != FieldType.Decimal)
        {
            return value.Value;
        }

        exact = TryParseDecimal((string)value.Value, out var number);
        return exact ? number : null;
    }

    /// <summary>
    /// Converts a stored value, as <see cref="RecordQueries"/> reads it, to the CLR type the
    /// interpreter reads. Returns false for a decimal that <see cref="decimal"/> cannot hold
    /// without rounding.
    /// </summary>
    private static bool TryFromStored(FieldModel field, JsonNode? node, out object? value)
    {
        value = null;
        if (node is null)
        {
            return true;
        }

        switch (field.Type)
        {
            case FieldType.Text or FieldType.Enum:
                value = node.GetValue<string>();
                return true;
            case FieldType.Integer:
                value = node.GetValue<long>();
                return true;
            case FieldType.Decimal:
                // The stored number text is kept as read, so every digit is still there.
                if (!TryParseDecimal(node.ToJsonString(), out var number))
                {
                    return false;
                }

                value = number;
                return true;
            case FieldType.Boolean:
                value = node.GetValue<bool>();
                return true;
            case FieldType.Date:
                value = DateOnly.ParseExact(node.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            case FieldType.DateTime:
                value = DateTimeOffset.ParseExact(
                    node.GetValue<string>(),
                    "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                return true;
            case FieldType.Reference:
                value = Guid.Parse(node.GetValue<string>());
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field.Type, "A field without a column has no stored value.");
        }
    }

    /// <summary>
    /// Parses plain-notation decimal text. <see cref="decimal"/> rounds digits it cannot hold, so
    /// the value counts only when it shows the same number as the text.
    /// </summary>
    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
        && Normalize(text) == Normalize(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Drops the trailing fraction zeros and the sign of zero, which do not change the number.</summary>
    private static string Normalize(string text)
    {
        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text is "-0" ? "0" : text;
    }
}
