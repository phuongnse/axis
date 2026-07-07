namespace Axis.Objects.Domain.ValueObjects;

public readonly record struct ObjectDefinitionId(Guid Value)
{
    public static ObjectDefinitionId New() => new(Guid.NewGuid());
    public static ObjectDefinitionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
