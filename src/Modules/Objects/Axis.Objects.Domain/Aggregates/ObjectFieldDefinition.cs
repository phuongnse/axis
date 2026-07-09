using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectFieldDefinition : Entity<ObjectFieldDefinitionId>
{
    private readonly List<ObjectFieldRule> _rules = [];

    public ObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }
    public ObjectFieldType FieldType { get; private set; }
    public IReadOnlyList<ObjectFieldRule> Rules => _rules.AsReadOnly();

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
        IReadOnlyList<ObjectFieldRule> rules)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
        FieldType = fieldType;
        _rules.AddRange(rules.OrderBy(rule => rule.Order));
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

        Result<IReadOnlyList<ObjectFieldRule>> rules = ObjectFieldRule.CreateMany(spec.Rules);
        if (rules.IsFailure)
            return Result.Failure<ObjectFieldDefinition>(rules.Error);

        return new ObjectFieldDefinition(
            id,
            key.Value,
            spec.Label.Trim(),
            spec.Order,
            spec.FieldType,
            rules.Value);
    }

    public ObjectFieldDefinition Snapshot() =>
        new(
            ObjectFieldDefinitionId.New(),
            Key,
            Label,
            Order,
            FieldType,
            _rules.Select(rule => rule.Snapshot()).ToList());

    internal void Apply(ObjectFieldDefinition source)
    {
        Key = source.Key;
        Label = source.Label;
        Order = source.Order;
        FieldType = source.FieldType;
        _rules.Clear();
        _rules.AddRange(source.Rules.Select(rule => rule.Snapshot()));
    }
}
