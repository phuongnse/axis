using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinitionVersionField : Entity<ObjectFieldDefinitionId>
{
    private readonly List<ObjectDefinitionVersionFieldRule> _rules = [];

    public ObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }
    public ObjectFieldType FieldType { get; private set; }
    public IReadOnlyList<ObjectDefinitionVersionFieldRule> Rules => _rules.AsReadOnly();

    private ObjectDefinitionVersionField(
        ObjectFieldDefinitionId id,
        ObjectFieldKey key,
        string label,
        int order,
        ObjectFieldType fieldType)
        : this(id, key, label, order, fieldType, [])
    {
    }

    private ObjectDefinitionVersionField(
        ObjectFieldDefinitionId id,
        ObjectFieldKey key,
        string label,
        int order,
        ObjectFieldType fieldType,
        IReadOnlyList<ObjectDefinitionVersionFieldRule> rules)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
        FieldType = fieldType;
        _rules.AddRange(rules.OrderBy(rule => rule.Order));
    }

    public static ObjectDefinitionVersionField FromCurrentDefinition(ObjectFieldDefinition field) =>
        new(
            ObjectFieldDefinitionId.New(),
            field.Key,
            field.Label,
            field.Order,
            field.FieldType,
            field.Rules
                .Select(ObjectDefinitionVersionFieldRule.FromCurrentRule)
                .ToList());
}
