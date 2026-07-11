namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectDefinitionId(Guid Value)
{
    public static BusinessObjectDefinitionId New() => new(Guid.NewGuid());
    public static BusinessObjectDefinitionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
