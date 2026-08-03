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
        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> result =
            ValidateAndCanonicalize(definition, values);
        return result.IsSuccess
            ? Result.Success()
            : result.ErrorCode == ErrorCodes.FieldValidation && result.FieldErrors is not null
                ? Result.FieldValidation(result.FieldErrors)
                : Result.Failure(result.ErrorCode ?? ErrorCodes.InvalidInput, result.Error);
    }

    public static Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> ValidateAndCanonicalize(
        BusinessObjectDefinitionVersion definition,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        if (values is null)
        {
            return Result.FieldValidation<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["values"] = ["Record values are required."],
                });
        }

        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);
        Dictionary<string, BusinessObjectDefinitionVersionField> fields = definition.Fields
            .ToDictionary(field => field.Key.Value, StringComparer.Ordinal);

        foreach (string? key in values.Keys)
        {
            if (key is null || !fields.ContainsKey(key))
                errors[key ?? "values"] = ["The field is not part of the published definition."];
        }

        Dictionary<string, IReadOnlyList<string>> canonical = new(StringComparer.Ordinal);
        foreach (BusinessObjectDefinitionVersionField field in definition.Fields.OrderBy(field => field.Order))
        {
            if (!values.TryGetValue(field.Key.Value, out IReadOnlyList<string>? fieldValues))
                continue;
            if (fieldValues is null)
            {
                errors[field.Key.Value] = ["The field values must be an array."];
                continue;
            }
            if (fieldValues.Count == 0)
            {
                canonical[field.Key.Value] = [];
                continue;
            }

            bool allowMultiple = field.FieldType == BusinessObjectFieldType.Choice &&
                field.ChoiceSelectionMode == BusinessObjectChoiceSelectionMode.Multiple;
            if (!allowMultiple && fieldValues.Count > 1)
            {
                errors[field.Key.Value] = ["The field accepts one value."];
                continue;
            }

            List<string> fieldErrors = [];
            List<string> canonicalValues = [];
            foreach (string? value in fieldValues)
            {
                if (value is null)
                {
                    fieldErrors.Add("The field value cannot be null.");
                    continue;
                }
                if (value.Length > 20_000)
                {
                    fieldErrors.Add("The value is too long.");
                    continue;
                }

                if (!TryCanonicalizeType(field, value, out string normalized))
                {
                    fieldErrors.Add($"The value is not a valid {field.FieldType} value.");
                    continue;
                }
                canonicalValues.Add(normalized);
            }

            if (field.FieldType == BusinessObjectFieldType.Choice)
            {
                HashSet<string> optionKeys = field.ChoiceOptions
                    .Select(option => option.Key.Value)
                    .ToHashSet(StringComparer.Ordinal);
                fieldErrors.AddRange(canonicalValues
                    .Where(value => !optionKeys.Contains(value))
                    .Select(_ => "The selected option is not part of the published definition."));
                if (allowMultiple && canonicalValues.Count != canonicalValues.Distinct(StringComparer.Ordinal).Count())
                    fieldErrors.Add("The field cannot contain duplicate options.");
            }

            if (fieldErrors.Count > 0)
            {
                errors[field.Key.Value] = fieldErrors.Distinct(StringComparer.Ordinal).ToArray();
                continue;
            }

            canonical[field.Key.Value] = canonicalValues;
        }

        return errors.Count == 0
            ? Result.Success<IReadOnlyDictionary<string, IReadOnlyList<string>>>(canonical)
            : Result.FieldValidation<IReadOnlyDictionary<string, IReadOnlyList<string>>>(errors);
    }

    private static bool TryCanonicalizeType(
        BusinessObjectDefinitionVersionField field,
        string value,
        out string canonical)
    {
        canonical = value;
        switch (field.FieldType)
        {
            case BusinessObjectFieldType.Text:
            case BusinessObjectFieldType.Choice:
                return true;
            case BusinessObjectFieldType.Integer:
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)
                    && SetCanonical(integer.ToString(CultureInfo.InvariantCulture), out canonical);
            case BusinessObjectFieldType.Decimal:
                return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue)
                    && SetCanonical(decimalValue.ToString("G29", CultureInfo.InvariantCulture), out canonical);
            case BusinessObjectFieldType.Date:
                return DateOnly.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly date)
                    && SetCanonical(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out canonical);
            case BusinessObjectFieldType.DateTime:
                return HasExplicitOffset(value) &&
                    DateTimeOffset.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTimeOffset dateTime) &&
                    SetCanonical(dateTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), out canonical);
            case BusinessObjectFieldType.Boolean:
                return bool.TryParse(value, out bool boolean)
                    && SetCanonical(boolean.ToString().ToLowerInvariant(), out canonical);
            default:
                return false;
        }
    }

    private static bool SetCanonical(string value, out string canonical)
    {
        canonical = value;
        return true;
    }

    private static bool HasExplicitOffset(string value)
    {
        string normalized = value.Trim();
        if (normalized.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            return true;
        if (normalized.Length < 6)
            return false;

        int signIndex = normalized.Length - 6;
        return (normalized[signIndex] == '+' || normalized[signIndex] == '-') &&
            char.IsDigit(normalized[signIndex + 1]) &&
            char.IsDigit(normalized[signIndex + 2]) &&
            normalized[signIndex + 3] == ':' &&
            char.IsDigit(normalized[signIndex + 4]) &&
            char.IsDigit(normalized[signIndex + 5]);
    }
}
