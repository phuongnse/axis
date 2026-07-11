namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectDefinitionVersionId(Guid Value)
{
    public static BusinessObjectDefinitionVersionId New() => new(Guid.NewGuid());
    public static BusinessObjectDefinitionVersionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
