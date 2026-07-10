namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectDefinitionVersionChoiceOptionId(Guid Value)
{
    public static BusinessObjectDefinitionVersionChoiceOptionId New() => new(Guid.NewGuid());
    public static BusinessObjectDefinitionVersionChoiceOptionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
