namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectDefinitionVersionFieldRuleId(Guid Value)
{
    public static BusinessObjectDefinitionVersionFieldRuleId New() => new(Guid.NewGuid());
    public static BusinessObjectDefinitionVersionFieldRuleId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
