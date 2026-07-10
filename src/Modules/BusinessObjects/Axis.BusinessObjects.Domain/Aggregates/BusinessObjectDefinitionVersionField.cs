using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersionField : Entity<BusinessObjectDefinitionVersionFieldId>
{
    private readonly List<BusinessObjectDefinitionVersionFieldRule> _rules = [];
    private readonly List<BusinessObjectDefinitionVersionChoiceOption> _choiceOptions = [];

    public BusinessObjectFieldDefinitionId SourceFieldDefinitionId { get; private set; }
    public BusinessObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }
    public BusinessObjectFieldType FieldType { get; private set; }
    public BusinessObjectChoiceSelectionMode? ChoiceSelectionMode { get; private set; }
    public IReadOnlyList<BusinessObjectDefinitionVersionChoiceOption> ChoiceOptions =>
        _choiceOptions.OrderBy(option => option.Order).ToArray();
    public IReadOnlyList<BusinessObjectDefinitionVersionFieldRule> Rules =>
        _rules.OrderBy(rule => rule.Order).ToArray();

    private BusinessObjectDefinitionVersionField(
        BusinessObjectDefinitionVersionFieldId id,
        BusinessObjectFieldDefinitionId sourceFieldDefinitionId,
        BusinessObjectFieldKey key,
        string label,
        int order,
        BusinessObjectFieldType fieldType)
        : this(id, sourceFieldDefinitionId, key, label, order, fieldType, null, [], [])
    {
    }

    private BusinessObjectDefinitionVersionField(
        BusinessObjectDefinitionVersionFieldId id,
        BusinessObjectFieldDefinitionId sourceFieldDefinitionId,
        BusinessObjectFieldKey key,
        string label,
        int order,
        BusinessObjectFieldType fieldType,
        BusinessObjectChoiceSelectionMode? choiceSelectionMode,
        IReadOnlyList<BusinessObjectDefinitionVersionChoiceOption> choiceOptions,
        IReadOnlyList<BusinessObjectDefinitionVersionFieldRule> rules)
        : base(id)
    {
        SourceFieldDefinitionId = sourceFieldDefinitionId;
        Key = key;
        Label = label;
        Order = order;
        FieldType = fieldType;
        ChoiceSelectionMode = choiceSelectionMode;
        _choiceOptions.AddRange(choiceOptions.OrderBy(option => option.Order));
        _rules.AddRange(rules.OrderBy(rule => rule.Order));
    }

    public static BusinessObjectDefinitionVersionField FromCurrentDefinition(BusinessObjectFieldDefinition field) =>
        new(
            BusinessObjectDefinitionVersionFieldId.New(),
            field.Id,
            field.Key,
            field.Label,
            field.Order,
            field.FieldType,
            field.ChoiceSelectionMode,
            field.ChoiceOptions
                .Select(BusinessObjectDefinitionVersionChoiceOption.FromCurrentOption)
                .ToArray(),
            field.Rules
                .Select(BusinessObjectDefinitionVersionFieldRule.FromCurrentRule)
                .ToArray());
}
