using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinitionVersionField : Entity<ObjectFieldDefinitionId>
{
    private readonly List<ObjectDefinitionVersionFieldVariant> _variants = [];

    public ObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }
    public ObjectFieldType FieldType { get; private set; }
    public IReadOnlyList<ObjectDefinitionVersionFieldVariant> Variants => _variants.AsReadOnly();

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
        IReadOnlyList<ObjectDefinitionVersionFieldVariant> variants)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
        FieldType = fieldType;
        _variants.AddRange(variants.OrderBy(variant => variant.Order));
    }

    public static ObjectDefinitionVersionField FromCurrentDefinition(ObjectFieldDefinition field) =>
        new(
            ObjectFieldDefinitionId.New(),
            field.Key,
            field.Label,
            field.Order,
            field.FieldType,
            field.Variants
                .Select(ObjectDefinitionVersionFieldVariant.FromCurrentVariant)
                .ToList());
}
