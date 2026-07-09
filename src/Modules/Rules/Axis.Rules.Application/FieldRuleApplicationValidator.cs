using System.Globalization;
using System.Text.RegularExpressions;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using DomainParameterType = Axis.Rules.Domain.FieldRuleParameterType;

namespace Axis.Rules.Application;

public sealed class FieldRuleApplicationValidator : IFieldRuleApplicationValidator
{
    private static readonly TimeSpan PatternValidationTimeout = TimeSpan.FromMilliseconds(250);

    public FieldRuleApplicationValidationResult ValidateFieldRuleApplication(
        string definitionKey,
        string fieldType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        FieldRuleDefinition? definition = SystemFieldRuleCatalog.Find(definitionKey);
        if (definition is null)
            return FieldRuleApplicationValidationResult.Invalid("Field rule definition is not supported.");

        string normalizedFieldType = fieldType.Trim();
        if (!definition.SupportedFieldTypes.Contains(normalizedFieldType, StringComparer.Ordinal))
            return FieldRuleApplicationValidationResult.Invalid(
                "Field rule is not compatible with the selected field type.");

        Dictionary<string, string[]> normalizedParameters = Normalize(parameters);
        FieldRuleApplicationValidationResult schema = ValidateSchema(definition, normalizedParameters);
        if (!schema.IsValid)
            return schema;

        return definition.Key.Value switch
        {
            FieldRuleDefinitionKeys.Required => FieldRuleApplicationValidationResult.Valid(),
            FieldRuleDefinitionKeys.NumericRange => ValidateNumericRange(normalizedFieldType, normalizedParameters),
            FieldRuleDefinitionKeys.DateRange => ValidateDateRange(normalizedParameters),
            FieldRuleDefinitionKeys.TextLength => ValidateTextLength(normalizedParameters),
            FieldRuleDefinitionKeys.TextPattern => ValidateTextPattern(normalizedParameters),
            FieldRuleDefinitionKeys.SingleSelectOptions => ValidateSingleSelectOptions(normalizedParameters),
            _ => FieldRuleApplicationValidationResult.Invalid("Field rule definition is not supported."),
        };
    }

    private static Dictionary<string, string[]> Normalize(
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters) =>
        parameters.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value.Select(value => value.Trim()).ToArray(),
            StringComparer.Ordinal);

    private static FieldRuleApplicationValidationResult ValidateSchema(
        FieldRuleDefinition definition,
        IReadOnlyDictionary<string, string[]> parameters)
    {
        Dictionary<string, FieldRuleParameterDefinition> schema = definition.Parameters
            .ToDictionary(parameter => parameter.Key, StringComparer.Ordinal);

        foreach ((string key, string[] values) in parameters)
        {
            if (!schema.TryGetValue(key, out FieldRuleParameterDefinition? parameter))
                return FieldRuleApplicationValidationResult.Invalid("Field rule parameter is not supported.");

            if (!parameter.AllowMultiple && values.Length > 1)
                return FieldRuleApplicationValidationResult.Invalid("Field rule parameter must have one value.");

            if (values.Any(string.IsNullOrWhiteSpace))
                return FieldRuleApplicationValidationResult.Invalid("Field rule parameter value is required.");

            foreach (string value in values)
            {
                FieldRuleApplicationValidationResult typed = ValidateType(parameter.Type, value);
                if (!typed.IsValid)
                    return typed;
            }
        }

        foreach (FieldRuleParameterDefinition parameter in definition.Parameters.Where(parameter => parameter.IsRequired))
        {
            if (!parameters.TryGetValue(parameter.Key, out string[]? values) || values.Length == 0)
                return FieldRuleApplicationValidationResult.Invalid("Field rule parameter is required.");
        }

        return FieldRuleApplicationValidationResult.Valid();
    }

    private static FieldRuleApplicationValidationResult ValidateType(
        DomainParameterType type,
        string value)
    {
        bool valid = type switch
        {
            DomainParameterType.Text => value.Length > 0,
            DomainParameterType.Integer => int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _),
            DomainParameterType.Decimal => decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _),
            DomainParameterType.Date => DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _),
            DomainParameterType.Boolean => bool.TryParse(value, out _),
            _ => false,
        };

        return valid
            ? FieldRuleApplicationValidationResult.Valid()
            : FieldRuleApplicationValidationResult.Invalid("Field rule parameter type is invalid.");
    }

    private static FieldRuleApplicationValidationResult ValidateNumericRange(
        string fieldType,
        IReadOnlyDictionary<string, string[]> parameters)
    {
        if (!TryOptionalDecimal(parameters, "min", out decimal? min)
            || !TryOptionalDecimal(parameters, "max", out decimal? max))
            return FieldRuleApplicationValidationResult.Invalid("Field rule parameter type is invalid.");

        if (min is null && max is null)
            return FieldRuleApplicationValidationResult.Invalid("Numeric range requires at least one bound.");

        if (min > max)
            return FieldRuleApplicationValidationResult.Invalid("Numeric range minimum cannot exceed maximum.");

        if (fieldType == "Integer" && (!IsWholeNumber(min) || !IsWholeNumber(max)))
            return FieldRuleApplicationValidationResult.Invalid(
                "Numeric range bounds for integer fields must be whole numbers.");

        return FieldRuleApplicationValidationResult.Valid();
    }

    private static FieldRuleApplicationValidationResult ValidateDateRange(
        IReadOnlyDictionary<string, string[]> parameters)
    {
        if (!TryOptionalDate(parameters, "min", out DateOnly? min)
            || !TryOptionalDate(parameters, "max", out DateOnly? max))
            return FieldRuleApplicationValidationResult.Invalid("Field rule parameter type is invalid.");

        if (min is null && max is null)
            return FieldRuleApplicationValidationResult.Invalid("Date range requires at least one bound.");

        return min > max
            ? FieldRuleApplicationValidationResult.Invalid("Date range minimum cannot exceed maximum.")
            : FieldRuleApplicationValidationResult.Valid();
    }

    private static FieldRuleApplicationValidationResult ValidateTextLength(
        IReadOnlyDictionary<string, string[]> parameters)
    {
        if (!TryOptionalInt(parameters, "min", out int? min)
            || !TryOptionalInt(parameters, "max", out int? max))
            return FieldRuleApplicationValidationResult.Invalid("Field rule parameter type is invalid.");

        if (min is null && max is null)
            return FieldRuleApplicationValidationResult.Invalid("Text length requires at least one bound.");

        if (min < 0 || max < 0)
            return FieldRuleApplicationValidationResult.Invalid("Text length bounds cannot be negative.");

        return min > max
            ? FieldRuleApplicationValidationResult.Invalid("Text length minimum cannot exceed maximum.")
            : FieldRuleApplicationValidationResult.Valid();
    }

    private static FieldRuleApplicationValidationResult ValidateTextPattern(
        IReadOnlyDictionary<string, string[]> parameters)
    {
        string pattern = parameters["pattern"].Single();
        try
        {
            _ = new Regex(pattern, RegexOptions.None, PatternValidationTimeout);
            return FieldRuleApplicationValidationResult.Valid();
        }
        catch (ArgumentException)
        {
            return FieldRuleApplicationValidationResult.Invalid("Text pattern is invalid.");
        }
    }

    private static FieldRuleApplicationValidationResult ValidateSingleSelectOptions(
        IReadOnlyDictionary<string, string[]> parameters)
    {
        string[] options = parameters["options"];
        if (options.Length == 0)
            return FieldRuleApplicationValidationResult.Invalid(
                "Single-select options require at least one value.");

        return options.Distinct(StringComparer.Ordinal).Count() == options.Length
            ? FieldRuleApplicationValidationResult.Valid()
            : FieldRuleApplicationValidationResult.Invalid("Single-select option values must be unique.");
    }

    private static bool TryOptionalDecimal(
        IReadOnlyDictionary<string, string[]> parameters,
        string key,
        out decimal? value)
    {
        value = null;
        if (!TrySingle(parameters, key, out string? raw))
            return true;

        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryOptionalInt(
        IReadOnlyDictionary<string, string[]> parameters,
        string key,
        out int? value)
    {
        value = null;
        if (!TrySingle(parameters, key, out string? raw))
            return true;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryOptionalDate(
        IReadOnlyDictionary<string, string[]> parameters,
        string key,
        out DateOnly? value)
    {
        value = null;
        if (!TrySingle(parameters, key, out string? raw))
            return true;

        if (!DateOnly.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TrySingle(
        IReadOnlyDictionary<string, string[]> parameters,
        string key,
        out string? value)
    {
        value = null;
        if (!parameters.TryGetValue(key, out string[]? values) || values.Length == 0)
            return false;

        value = values.Single();
        return true;
    }

    private static bool IsWholeNumber(decimal? value) =>
        value is null || decimal.Truncate(value.Value) == value.Value;
}
