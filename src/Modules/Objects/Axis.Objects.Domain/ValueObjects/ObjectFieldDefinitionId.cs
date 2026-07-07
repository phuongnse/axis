namespace Axis.Objects.Domain.ValueObjects;

public readonly record struct ObjectFieldDefinitionId(Guid Value)
{
    public static ObjectFieldDefinitionId New() => new(Guid.NewGuid());
    public static ObjectFieldDefinitionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
