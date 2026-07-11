namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectDefinitionVersionFieldId(Guid Value)
{
    public static BusinessObjectDefinitionVersionFieldId New() => new(Guid.NewGuid());
    public static BusinessObjectDefinitionVersionFieldId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
