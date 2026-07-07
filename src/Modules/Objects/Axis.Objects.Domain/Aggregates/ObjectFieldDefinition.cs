using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectFieldDefinition : Entity<ObjectFieldDefinitionId>
{
    public ObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }

    private ObjectFieldDefinition(
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

    public static Result<ObjectFieldDefinition> Create(
        ObjectFieldDefinitionId id,
        ObjectFieldDefinitionSpec spec)
    {
        Result<ObjectFieldKey> key = ObjectFieldKey.Create(spec.FieldKey);
        if (key.IsFailure)
            return Result.Failure<ObjectFieldDefinition>(key.Error);

        if (string.IsNullOrWhiteSpace(spec.Label))
            return Result.Failure<ObjectFieldDefinition>("Field label is required.");

        return new ObjectFieldDefinition(
            id,
            key.Value,
            spec.Label.Trim(),
            spec.Order);
    }

    public ObjectFieldDefinition Snapshot() =>
        new(
            ObjectFieldDefinitionId.New(),
            Key,
            Label,
            Order);

    internal void Apply(ObjectFieldDefinition source)
    {
        Key = source.Key;
        Label = source.Label;
        Order = source.Order;
    }
}
