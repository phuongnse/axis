using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Rules.Contracts;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRuleBindingContextSchema
{
    public const string RecordValueKey = "record.value";

    public static IReadOnlyDictionary<string, RuleBindingContextValueSchema> For(
        BusinessObjectFieldType fieldType,
        BusinessObjectChoiceSelectionMode? choiceSelectionMode = null)
    {
        RuleValueType valueType = fieldType switch
        {
            BusinessObjectFieldType.Text => RuleValueType.Text,
            BusinessObjectFieldType.Integer => RuleValueType.Integer,
            BusinessObjectFieldType.Decimal => RuleValueType.Decimal,
            BusinessObjectFieldType.Date => RuleValueType.Date,
            BusinessObjectFieldType.DateTime => RuleValueType.DateTime,
            BusinessObjectFieldType.Boolean => RuleValueType.Boolean,
            BusinessObjectFieldType.Choice => RuleValueType.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, "Field type is not supported."),
        };
        bool allowMultiple = fieldType == BusinessObjectFieldType.Choice &&
            choiceSelectionMode == BusinessObjectChoiceSelectionMode.Multiple;
        return new Dictionary<string, RuleBindingContextValueSchema>(StringComparer.Ordinal)
        {
            [RecordValueKey] = new(valueType, allowMultiple),
        };
    }

    public static IReadOnlyList<string> RequiredKeys { get; } = [RecordValueKey];
}
