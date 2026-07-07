using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinitionVersionField : Entity<ObjectFieldDefinitionId>
{
    public ObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }

    private ObjectDefinitionVersionField(
        ObjectFieldDefinitionId id,
        ObjectFieldKey key,
        string label,
        int order)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
    }

    public static ObjectDefinitionVersionField FromDraft(ObjectFieldDefinition field) =>
        new(
            ObjectFieldDefinitionId.New(),
            field.Key,
            field.Label,
            field.Order);
}
