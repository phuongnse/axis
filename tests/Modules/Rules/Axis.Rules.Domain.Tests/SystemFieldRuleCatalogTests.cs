using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class SystemFieldRuleCatalogTests
{
    [Fact]
    public void Definitions_WhenRead_ReturnStableSystemFieldRuleCatalog()
    {
        SystemFieldRuleCatalog.Definitions.Select(definition => definition.Key.Value)
            .Should().Equal(
                FieldRuleDefinitionKeys.Required,
                FieldRuleDefinitionKeys.NumericRange,
                FieldRuleDefinitionKeys.DateRange,
                FieldRuleDefinitionKeys.TextLength,
                FieldRuleDefinitionKeys.TextPattern,
                FieldRuleDefinitionKeys.SingleSelectOptions);
    }

    [Fact]
    public void Definitions_WhenRead_HaveUniqueNormalizedKeysAndParameterSchemas()
    {
        IReadOnlyList<FieldRuleDefinition> definitions = SystemFieldRuleCatalog.Definitions;

        definitions.Select(definition => definition.Key.Value)
            .Should().OnlyHaveUniqueItems();
        definitions.Should().OnlyContain(definition => definition.SupportedFieldTypes.Count > 0);
        definitions.Should().OnlyContain(definition =>
            definition.Parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count()
            == definition.Parameters.Count);

        FieldRuleDefinition options = definitions.Single(
            definition => definition.Key.Value == FieldRuleDefinitionKeys.SingleSelectOptions);
        options.Parameters.Should().ContainSingle(parameter =>
            parameter.Key == "options" &&
            parameter.Type == FieldRuleParameterType.Text &&
            parameter.IsRequired &&
            parameter.AllowMultiple);
    }

    [Fact]
    public void Find_WhenKeyIsUnknown_ReturnsNull()
    {
        SystemFieldRuleCatalog.Find("field.unknown").Should().BeNull();
    }
}
