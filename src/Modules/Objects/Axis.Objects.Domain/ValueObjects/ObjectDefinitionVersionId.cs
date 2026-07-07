namespace Axis.Objects.Domain.ValueObjects;

public readonly record struct ObjectDefinitionVersionId(Guid Value)
{
    public static ObjectDefinitionVersionId New() => new(Guid.NewGuid());
    public static ObjectDefinitionVersionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
