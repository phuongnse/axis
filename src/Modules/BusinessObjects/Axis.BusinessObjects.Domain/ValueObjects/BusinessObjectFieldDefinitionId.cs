namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectFieldDefinitionId(Guid Value)
{
    public static BusinessObjectFieldDefinitionId New() => new(Guid.NewGuid());
    public static BusinessObjectFieldDefinitionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
