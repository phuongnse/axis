using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectFieldDefinition : Entity<ObjectFieldDefinitionId>
{
    private readonly List<ObjectFieldVariant> _variants = [];

    public ObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }
    public ObjectFieldType FieldType { get; private set; }
    public IReadOnlyList<ObjectFieldVariant> Variants => _variants.AsReadOnly();

    private ObjectFieldDefinition(
        ObjectFieldDefinitionId id,
        ObjectFieldKey key,
        string label,
        int order,
        ObjectFieldType fieldType)
        : this(id, key, label, order, fieldType, [])
    {
    }

    private ObjectFieldDefinition(
        ObjectFieldDefinitionId id,
        ObjectFieldKey key,
        string label,
        int order,
        ObjectFieldType fieldType,
        IReadOnlyList<ObjectFieldVariant> variants)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
        FieldType = fieldType;
        _variants.AddRange(variants.OrderBy(variant => variant.Order));
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

        if (!Enum.IsDefined(spec.FieldType))
            return Result.Failure<ObjectFieldDefinition>("Field type is not supported.");

        Result<IReadOnlyList<ObjectFieldVariant>> variants =
            ObjectFieldVariant.CreateMany(spec.FieldType, spec.Variants);
        if (variants.IsFailure)
            return Result.Failure<ObjectFieldDefinition>(variants.Error);

        return new ObjectFieldDefinition(
            id,
            key.Value,
            spec.Label.Trim(),
            spec.Order,
            spec.FieldType,
            variants.Value);
    }

    public ObjectFieldDefinition Snapshot() =>
        new(
            ObjectFieldDefinitionId.New(),
            Key,
            Label,
            Order,
            FieldType,
            _variants.Select(variant => variant.Snapshot()).ToList());

    internal void Apply(ObjectFieldDefinition source)
    {
        Key = source.Key;
        Label = source.Label;
        Order = source.Order;
        FieldType = source.FieldType;
        _variants.Clear();
        _variants.AddRange(source.Variants.Select(variant => variant.Snapshot()));
    }
}
