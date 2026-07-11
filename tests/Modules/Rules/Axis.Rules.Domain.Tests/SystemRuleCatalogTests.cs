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
}
