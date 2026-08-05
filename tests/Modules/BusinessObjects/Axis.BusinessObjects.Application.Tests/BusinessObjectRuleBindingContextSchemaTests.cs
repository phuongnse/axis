using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Rules.Contracts;
using FluentAssertions;

namespace Axis.BusinessObjects.Application.Tests;

public sealed class BusinessObjectRuleBindingContextSchemaTests
{
    [Theory]
    [InlineData(BusinessObjectFieldType.Text, RuleValueType.Text)]
    [InlineData(BusinessObjectFieldType.Integer, RuleValueType.Integer)]
    [InlineData(BusinessObjectFieldType.Decimal, RuleValueType.Decimal)]
    [InlineData(BusinessObjectFieldType.Date, RuleValueType.Date)]
    [InlineData(BusinessObjectFieldType.DateTime, RuleValueType.DateTime)]
    [InlineData(BusinessObjectFieldType.Boolean, RuleValueType.Boolean)]
    public void For_ScalarBusinessObjectTypes_ProjectsRecordValueWithMatchingRuleType(
        BusinessObjectFieldType fieldType,
        RuleValueType expectedType)
    {
        IReadOnlyDictionary<string, RuleBindingContextValueSchema> schema =
            BusinessObjectRuleBindingContextSchema.For(fieldType);

        schema.Should().ContainSingle();
        schema[BusinessObjectRuleBindingContextSchema.RecordValueKey].Type.Should().Be(expectedType);
        schema[BusinessObjectRuleBindingContextSchema.RecordValueKey].AllowMultiple.Should().BeFalse();
    }

    [Theory]
    [InlineData(BusinessObjectChoiceSelectionMode.Single, false)]
    [InlineData(BusinessObjectChoiceSelectionMode.Multiple, true)]
    public void For_Choice_ProjectsTextAndItsSelectionCardinality(
        BusinessObjectChoiceSelectionMode selectionMode,
        bool allowMultiple)
    {
        IReadOnlyDictionary<string, RuleBindingContextValueSchema> schema =
            BusinessObjectRuleBindingContextSchema.For(BusinessObjectFieldType.Choice, selectionMode);

        schema[BusinessObjectRuleBindingContextSchema.RecordValueKey].Type.Should().Be(RuleValueType.Text);
        schema[BusinessObjectRuleBindingContextSchema.RecordValueKey].AllowMultiple.Should().Be(allowMultiple);
    }
}
