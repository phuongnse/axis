using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public static class SystemFieldRuleCatalog
{
    public static IReadOnlyList<FieldRuleDefinition> Definitions { get; } =
    [
        Definition(
            "field.required",
            "Required value",
            "Requires a value for the field.",
            ["Text", "Integer", "Decimal", "Date", "Boolean", "SingleSelect"],
            []),
        Definition(
            "field.numeric_range",
            "Numeric range",
            "Constrains numeric values to optional minimum and maximum bounds.",
            ["Integer", "Decimal"],
            [
                Parameter("min", FieldRuleParameterType.Decimal, isRequired: false),
                Parameter("max", FieldRuleParameterType.Decimal, isRequired: false),
            ]),
        Definition(
            "field.date_range",
            "Date range",
            "Constrains date values to optional earliest and latest dates.",
            ["Date"],
            [
                Parameter("min", FieldRuleParameterType.Date, isRequired: false),
                Parameter("max", FieldRuleParameterType.Date, isRequired: false),
            ]),
        Definition(
            "field.text_length",
            "Text length",
            "Constrains text values to optional minimum and maximum lengths.",
            ["Text"],
            [
                Parameter("min", FieldRuleParameterType.Integer, isRequired: false),
                Parameter("max", FieldRuleParameterType.Integer, isRequired: false),
            ]),
        Definition(
            "field.text_pattern",
            "Text pattern",
            "Requires text values to match a regular expression pattern.",
            ["Text"],
            [Parameter("pattern", FieldRuleParameterType.Text, isRequired: true)]),
        Definition(
            "field.single_select_options",
            "Single-select options",
            "Defines the allowed option values for a single-select field.",
            ["SingleSelect"],
            [Parameter("options", FieldRuleParameterType.Text, isRequired: true, allowMultiple: true)]),
    ];

    public static FieldRuleDefinition? Find(string definitionKey) =>
        Definitions.FirstOrDefault(
            definition => definition.Key.Value.Equals(definitionKey.Trim(), StringComparison.Ordinal));

    private static FieldRuleDefinition Definition(
        string definitionKey,
        string displayName,
        string description,
        IReadOnlyList<string> supportedFieldTypes,
        IReadOnlyList<FieldRuleParameterDefinition> parameters)
    {
        Result<FieldRuleDefinition> result = FieldRuleDefinition.Create(
            definitionKey,
            displayName,
            description,
            supportedFieldTypes,
            parameters);

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error);
    }

    private static FieldRuleParameterDefinition Parameter(
        string key,
        FieldRuleParameterType type,
        bool isRequired,
        bool allowMultiple = false)
    {
        Result<FieldRuleParameterDefinition> result = FieldRuleParameterDefinition.Create(
            key,
            type,
            isRequired,
            allowMultiple);

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error);
    }
}
