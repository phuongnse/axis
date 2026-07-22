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
            [],
            Predicate(
                "required",
                RulePredicateOperator.Equal,
                Function(RuleExpressionFunction.IsBlank, Value()),
                Boolean(true))),
        Definition(
            "field.numeric_range",
            "Numeric range",
            "Constrains numeric values to optional minimum and maximum bounds.",
            ["Integer", "Decimal"],
            [
                Parameter("min", RuleValueType.Decimal, isRequired: false),
                Parameter("max", RuleValueType.Decimal, isRequired: false),
            ],
            Range("numeric-range", Function(RuleExpressionFunction.ToDecimal, Value()))),
        Definition(
            "field.decimal_precision",
            "Decimal precision",
            "Constrains decimal precision and scale.",
            ["Decimal"],
            [
                Parameter("precision", RuleValueType.Integer, isRequired: true),
                Parameter("scale", RuleValueType.Integer, isRequired: true),
            ],
            Any(
                "decimal-precision",
                Predicate(
                    "decimal-precision-digits",
                    RulePredicateOperator.GreaterThan,
                    Function(RuleExpressionFunction.Precision, Value()),
                    ParameterOperand("precision")),
                Predicate(
                    "decimal-precision-scale",
                    RulePredicateOperator.GreaterThan,
                    Function(RuleExpressionFunction.Scale, Value()),
                    ParameterOperand("scale")))),
        Definition(
            "field.date_range",
            "Date range",
            "Constrains calendar dates to optional earliest and latest dates.",
            ["Date"],
            [
                Parameter("min", RuleValueType.Date, isRequired: false),
                Parameter("max", RuleValueType.Date, isRequired: false),
            ],
            Range("date-range", Value())),
        Definition(
            "field.datetime_range",
            "Date and time range",
            "Constrains date-and-time instants to optional earliest and latest values.",
            ["DateTime"],
            [
                Parameter("min", RuleValueType.DateTime, isRequired: false),
                Parameter("max", RuleValueType.DateTime, isRequired: false),
            ],
            Range("datetime-range", Value())),
        Definition(
            "field.text_length",
            "Text length",
            "Constrains text values to optional minimum and maximum lengths.",
            ["Text"],
            [
                Parameter("min", RuleValueType.Integer, isRequired: false),
                Parameter("max", RuleValueType.Integer, isRequired: false),
            ],
            Range("text-length", Function(RuleExpressionFunction.Length, Value()))),
        Definition(
            "field.text_pattern",
            "Text pattern",
            "Requires text values to match a system-supported regular expression pattern.",
            ["Text"],
            [Parameter("pattern", RuleValueType.Text, isRequired: true)],
            Predicate(
                "text-pattern",
                RulePredicateOperator.Equal,
                Function(
                    RuleExpressionFunction.MatchesPattern,
                    Value(),
                    ParameterOperand("pattern")),
                Boolean(false))),
        Definition(
            "field.text_format",
            "Text format",
            "Requires text values to use a supported enterprise format.",
            ["Text"],
            [Parameter("format", RuleValueType.Text, isRequired: true, allowedValues: ["Email", "Url", "Uuid"])],
            Predicate(
                "text-format",
                RulePredicateOperator.Equal,
                Function(
                    RuleExpressionFunction.HasFormat,
                    Value(),
                    ParameterOperand("format")),
                Boolean(false))),
        Definition(
            "field.choice_selection_count",
            "Choice selection count",
            "Constrains the number of selected values for a multiple-choice field.",
            ["Choice"],
            [
                Parameter("min", RuleValueType.Integer, isRequired: false),
                Parameter("max", RuleValueType.Integer, isRequired: false),
            ],
            Range("choice-selection-count", Function(RuleExpressionFunction.Count, Value())),
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
        RuleConditionNode condition,
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
            parameters,
            RuleExpressionLanguage.Version,
            condition,
            ValidationOutcome(key, displayName));
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

    private static RuleConditionNode Range(string prefix, RuleOperand value) =>
        Any(
            prefix,
            All(
                $"{prefix}-minimum",
                Predicate(
                    $"{prefix}-minimum-set",
                    RulePredicateOperator.IsNotNull,
                    ParameterOperand("min")),
                Predicate(
                    $"{prefix}-below-minimum",
                    RulePredicateOperator.LessThan,
                    value,
                    ParameterOperand("min"))),
            All(
                $"{prefix}-maximum",
                Predicate(
                    $"{prefix}-maximum-set",
                    RulePredicateOperator.IsNotNull,
                    ParameterOperand("max")),
                Predicate(
                    $"{prefix}-above-maximum",
                    RulePredicateOperator.GreaterThan,
                    value,
                    ParameterOperand("max"))));

    private static RuleConditionGroup All(string nodeId, params RuleConditionNode[] children) =>
        RuleConditionGroup.Create(nodeId, RuleLogicalOperator.All, children).Value;

    private static RuleConditionGroup Any(string nodeId, params RuleConditionNode[] children) =>
        RuleConditionGroup.Create(nodeId, RuleLogicalOperator.Any, children).Value;

    private static RulePredicateCondition Predicate(
        string nodeId,
        RulePredicateOperator @operator,
        RuleOperand left,
        RuleOperand? right = null) =>
        RulePredicateCondition.Create(nodeId, @operator, left, right).Value;

    private static RuleOperand Value() => RuleOperand.Context("field.value").Value;

    private static RuleOperand ParameterOperand(string key) => RuleOperand.Parameter(key).Value;

    private static RuleOperand Function(
        RuleExpressionFunction function,
        params RuleOperand[] arguments) =>
        RuleOperand.Function(function, arguments).Value;

    private static RuleOperand Boolean(bool value) =>
        RuleOperand.LiteralValue(
            RuleValue.Create(RuleValueType.Boolean, [value.ToString()]).Value).Value;

    private static RuleOutcome ValidationOutcome(string key, string displayName) =>
        RuleValidationOutcome.Create(
            $"{key}.failed",
            RuleSeverity.Error,
            $"{displayName} validation failed.").Value;
}
