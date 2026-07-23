using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class SystemRuleCatalogTests
{
    [Fact]
    public void Definitions_WhenRead_ReturnEnterpriseSystemRuleCatalog()
    {
        SystemRuleCatalog.Definitions.Select(definition => definition.Key.Value)
            .Should().Equal(
                "field.required",
                "field.numeric_range",
                "field.decimal_precision",
                "field.date_range",
                "field.datetime_range",
                "field.text_length",
                "field.text_pattern",
                "field.text_format",
                "field.choice_selection_count");
    }

    [Fact]
    public void Definitions_WhenRead_HaveVersionedNormalizedMetadata()
    {
        IReadOnlyList<SystemRuleDefinition> definitions = SystemRuleCatalog.Definitions;

        definitions.Select(definition => (definition.Key.Value, definition.Version))
            .Should().OnlyHaveUniqueItems();
        definitions.Should().OnlyContain(definition =>
            definition.Version == 1 &&
            definition.Origin == RuleOrigin.System &&
            definition.Scope == RuleScope.Field &&
            definition.Status == RuleLifecycleStatus.Published &&
            definition.Applicability.TargetTypeKeys.Count > 0);
        definitions.Should().OnlyContain(definition =>
            definition.Parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count()
            == definition.Parameters.Count);

        SystemRuleDefinition textFormat = definitions.Single(
            definition => definition.Key.Value == "field.text_format");
        textFormat.Parameters.Should().ContainSingle(parameter =>
            parameter.Key == "format" &&
            parameter.Type == RuleValueType.Text &&
            parameter.AllowedValues.Count == 3 &&
            parameter.AllowedValues[0] == "Email" &&
            parameter.AllowedValues[1] == "Url" &&
            parameter.AllowedValues[2] == "Uuid");

        SystemRuleDefinition choiceCount = definitions.Single(
            definition => definition.Key.Value == "field.choice_selection_count");
        choiceCount.Applicability.ConfigurationConstraints["selection_mode"]
            .Should().Equal("Multiple");
    }

    [Fact]
    public void Definitions_WhenRead_OwnValidSharedExpressionsAndOutcomes()
    {
        foreach (SystemRuleDefinition definition in SystemRuleCatalog.Definitions)
        {
            definition.ExpressionLanguageVersion.Should().Be(RuleExpressionLanguage.Version);
            definition.Condition.Should().NotBeNull();
            definition.Outcome.Kind.Should().Be(definition.OutcomeKind);

            foreach (string targetType in definition.Applicability.TargetTypeKeys)
            {
                RuleContextSchema schema = SystemSchema(
                    targetType,
                    allowMultiple: definition.Key.Value == "field.choice_selection_count");

                RuleDefinitionValidator.Validate(
                        schema,
                        definition.Parameters,
                        definition.Condition,
                        definition.Outcome,
                        definition.OutcomeKind)
                    .IsSuccess.Should().BeTrue(
                        $"{definition.Key.Value} must be valid for {targetType}");
            }
        }
    }

    [Fact]
    public void Definitions_WhenEvaluated_UseRegisteredFunctionsForSystemSemantics()
    {
        SystemRuleDefinition required = SystemRuleCatalog.Find("field.required", 1)!;
        RuleContextSchema textSchema = SystemSchema("Text");
        RuleConditionEvaluator.Evaluate(
                required.Condition,
                textSchema,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["field.value"] = Value(RuleValueType.Text, "   "),
                })
            .Value.IsMatch.Should().BeTrue();

        SystemRuleDefinition precision = SystemRuleCatalog.Find("field.decimal_precision", 1)!;
        RuleConditionEvaluator.Evaluate(
                precision.Condition,
                SystemSchema("Decimal"),
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["field.value"] = Value(RuleValueType.Decimal, "123.456"),
                },
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["precision"] = Value(RuleValueType.Integer, "5"),
                    ["scale"] = Value(RuleValueType.Integer, "2"),
                })
            .Value.IsMatch.Should().BeTrue();

        SystemRuleDefinition format = SystemRuleCatalog.Find("field.text_format", 1)!;
        RuleConditionEvaluator.Evaluate(
                format.Condition,
                textSchema,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["field.value"] = Value(RuleValueType.Text, "not-an-email"),
                },
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["format"] = Value(RuleValueType.Text, "Email"),
                })
            .Value.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void DecimalPrecision_WhenValueRetainsFractionalZeros_CountsRetainedDigits()
    {
        SystemRuleDefinition precision = SystemRuleCatalog.Find("field.decimal_precision", 1)!;

        RuleConditionEvaluation result = RuleConditionEvaluator.Evaluate(
            precision.Condition,
            SystemSchema("Decimal"),
            new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                ["field.value"] = Value(RuleValueType.Decimal, "1.50"),
            },
            new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                ["precision"] = Value(RuleValueType.Integer, "2"),
                ["scale"] = Value(RuleValueType.Integer, "2"),
            }).Value;

        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Find_WhenVersionIsUnknown_ReturnsNull()
    {
        SystemRuleCatalog.Find("field.required", version: 2).Should().BeNull();
    }

    [Fact]
    public void Definitions_WhenCastToMutableCollections_RejectMutation()
    {
        SystemRuleDefinition definition = SystemRuleCatalog.Definitions[0];

        Action mutateCatalog = () => ((IList<SystemRuleDefinition>)SystemRuleCatalog.Definitions).Clear();
        Action mutateTargets = () => ((IList<string>)definition.Applicability.TargetTypeKeys).Clear();
        Action mutateParameters = () => ((IList<RuleParameterDefinition>)definition.Parameters).Clear();

        mutateCatalog.Should().Throw<NotSupportedException>();
        mutateTargets.Should().Throw<NotSupportedException>();
        mutateParameters.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Applicability_WhenNormalizedConfigurationKeysCollide_ReturnsFailure()
    {
        Result<RuleApplicability> result = RuleApplicability.Create(
            ["Text"],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["mode"] = ["One"],
                [" mode "] = ["Two"],
            });

        result.IsFailure.Should().BeTrue();
    }

    private static RuleContextSchema SystemSchema(string targetType, bool allowMultiple = false) =>
        RuleContextSchema.Create(
            $"system.field.{targetType.ToLowerInvariant()}",
            1,
            RuleScope.Field,
            $"{targetType} field",
            [
                RuleContextField.Create(
                    "field.value",
                    "Value",
                    targetType switch
                    {
                        "Integer" => RuleValueType.Integer,
                        "Decimal" => RuleValueType.Decimal,
                        "Date" => RuleValueType.Date,
                        "DateTime" => RuleValueType.DateTime,
                        "Boolean" => RuleValueType.Boolean,
                        _ => RuleValueType.Text,
                    },
                    allowMultiple).Value,
            ]).Value;

    private static RuleValue Value(RuleValueType type, string value) =>
        RuleValue.Create(type, [value]).Value;
}
