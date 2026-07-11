using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public static class SystemRuleCatalog
{
    public static IReadOnlyList<SystemRuleDefinition> Definitions { get; } = Array.AsReadOnly<SystemRuleDefinition>(
    [
        Definition(
            "field.required",
            "Required value",
            "Requires a value for the field.",
            ["Text", "Integer", "Decimal", "Date", "DateTime", "Boolean", "Choice"],
            []),
        Definition(
            "field.numeric_range",
            "Numeric range",
            "Constrains numeric values to optional minimum and maximum bounds.",
            ["Integer", "Decimal"],
            [
                Parameter("min", RuleValueType.Decimal, isRequired: false),
                Parameter("max", RuleValueType.Decimal, isRequired: false),
            ]),
        Definition(
            "field.decimal_precision",
            "Decimal precision",
            "Constrains decimal precision and scale.",
            ["Decimal"],
            [
                Parameter("precision", RuleValueType.Integer, isRequired: true),
                Parameter("scale", RuleValueType.Integer, isRequired: true),
            ]),
        Definition(
            "field.date_range",
            "Date range",
            "Constrains calendar dates to optional earliest and latest dates.",
            ["Date"],
            [
                Parameter("min", RuleValueType.Date, isRequired: false),
                Parameter("max", RuleValueType.Date, isRequired: false),
            ]),
        Definition(
            "field.datetime_range",
            "Date and time range",
            "Constrains date-and-time instants to optional earliest and latest values.",
            ["DateTime"],
            [
                Parameter("min", RuleValueType.DateTime, isRequired: false),
                Parameter("max", RuleValueType.DateTime, isRequired: false),
            ]),
        Definition(
            "field.text_length",
            "Text length",
            "Constrains text values to optional minimum and maximum lengths.",
            ["Text"],
            [
                Parameter("min", RuleValueType.Integer, isRequired: false),
                Parameter("max", RuleValueType.Integer, isRequired: false),
            ]),
        Definition(
            "field.text_pattern",
            "Text pattern",
            "Requires text values to match a system-supported regular expression pattern.",
            ["Text"],
            [Parameter("pattern", RuleValueType.Text, isRequired: true)]),
        Definition(
            "field.text_format",
            "Text format",
            "Requires text values to use a supported enterprise format.",
            ["Text"],
            [Parameter("format", RuleValueType.Text, isRequired: true, allowedValues: ["Email", "Url", "Uuid"])]),
        Definition(
            "field.choice_selection_count",
            "Choice selection count",
            "Constrains the number of selected values for a multiple-choice field.",
            ["Choice"],
            [
                Parameter("min", RuleValueType.Integer, isRequired: false),
                Parameter("max", RuleValueType.Integer, isRequired: false),
            ],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["selection_mode"] = ["Multiple"],
            }),
    ]);

    public static SystemRuleDefinition? Find(string key, int version) =>
        Definitions.SingleOrDefault(definition =>
            definition.Key.Value.Equals(key?.Trim(), StringComparison.Ordinal) &&
            definition.Version == version);

    private static SystemRuleDefinition Definition(
        string key,
        string displayName,
        string description,
        IReadOnlyList<string> targetTypes,
        IReadOnlyList<RuleParameterDefinition> parameters,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? configurationConstraints = null)
    {
        Result<RuleApplicability> applicability = RuleApplicability.Create(
            targetTypes,
            configurationConstraints);
        if (applicability.IsFailure)
            throw new InvalidOperationException(applicability.Error);

        Result<SystemRuleDefinition> definition = SystemRuleDefinition.Create(
            key,
            version: 1,
            displayName,
            description,
            RuleScope.Field,
            RuleOutcomeKind.Validation,
            applicability.Value,
            parameters);
        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(definition.Error);
    }

    private static RuleParameterDefinition Parameter(
        string key,
        RuleValueType type,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null)
    {
        Result<RuleParameterDefinition> parameter = RuleParameterDefinition.Create(
            key,
            type,
            isRequired,
            allowMultiple,
            allowedValues);
        return parameter.IsSuccess
            ? parameter.Value
            : throw new InvalidOperationException(parameter.Error);
    }
}
