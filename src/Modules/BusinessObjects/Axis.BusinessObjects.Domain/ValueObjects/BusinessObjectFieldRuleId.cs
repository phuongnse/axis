namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectFieldRuleId(Guid Value)
{
    public static BusinessObjectFieldRuleId New() => new(Guid.NewGuid());
    public static BusinessObjectFieldRuleId From(Guid value) => new(value);
}
