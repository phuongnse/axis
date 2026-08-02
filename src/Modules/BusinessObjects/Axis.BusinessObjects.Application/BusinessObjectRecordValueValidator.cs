using System.Globalization;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRecordValueValidator
{
    public static Result Validate(
        BusinessObjectDefinitionVersion definition,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);
        Dictionary<string, BusinessObjectDefinitionVersionField> fields = definition.Fields
            .ToDictionary(field => field.Key.Value, StringComparer.Ordinal);

        foreach (string key in values.Keys)
        {
            if (!fields.ContainsKey(key))
                errors[key] = ["The field is not part of the published definition."];
        }

        foreach (BusinessObjectDefinitionVersionField field in definition.Fields.OrderBy(field => field.Order))
        {
            if (!values.TryGetValue(field.Key.Value, out IReadOnlyList<string>? fieldValues))
                continue;
            if (fieldValues.Count == 0)
                continue;

            bool allowMultiple = field.FieldType == BusinessObjectFieldType.Choice &&
                field.ChoiceSelectionMode == BusinessObjectChoiceSelectionMode.Multiple;
            if (!allowMultiple && fieldValues.Count > 1)
            {
                errors[field.Key.Value] = ["The field accepts one value."];
                continue;
            }

            List<string> fieldErrors = [];
            foreach (string value in fieldValues)
            {
                if (value.Length > 20_000)
                {
                    fieldErrors.Add("The value is too long.");
                    continue;
                }

                if (!IsValidType(field, value))
                    fieldErrors.Add($"The value is not a valid {field.FieldType} value.");
            }

            if (field.FieldType == BusinessObjectFieldType.Choice)
            {
                HashSet<string> optionKeys = field.ChoiceOptions
                    .Select(option => option.Key.Value)
                    .ToHashSet(StringComparer.Ordinal);
                fieldErrors.AddRange(fieldValues
                    .Where(value => !optionKeys.Contains(value))
                    .Select(_ => "The selected option is not part of the published definition."));
            }

            if (fieldErrors.Count > 0)
                errors[field.Key.Value] = fieldErrors.Distinct(StringComparer.Ordinal).ToArray();
        }

        return errors.Count == 0 ? Result.Success() : Result.FieldValidation(errors);
    }

    private static bool IsValidType(BusinessObjectDefinitionVersionField field, string value) => field.FieldType switch
    {
        BusinessObjectFieldType.Text or BusinessObjectFieldType.Choice => true,
        BusinessObjectFieldType.Integer => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        BusinessObjectFieldType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        BusinessObjectFieldType.Date => DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _),
        BusinessObjectFieldType.DateTime => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out _),
        BusinessObjectFieldType.Boolean => bool.TryParse(value, out _),
        _ => false,
    };
}
