using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed record BusinessObjectFieldDefinitionSpec(
    string FieldKey,
    string Label,
    int Order,
    BusinessObjectFieldType FieldType = BusinessObjectFieldType.Text,
    IReadOnlyList<BusinessObjectFieldRuleSpec>? Rules = null,
    BusinessObjectChoiceFieldConfigurationSpec? ChoiceConfiguration = null,
    BusinessObjectFieldDefinitionId? Id = null);
