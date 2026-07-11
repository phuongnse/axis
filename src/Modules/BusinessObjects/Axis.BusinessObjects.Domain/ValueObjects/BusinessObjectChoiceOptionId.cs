namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectChoiceOptionId(Guid Value)
{
    public static BusinessObjectChoiceOptionId New() => new(Guid.NewGuid());
    public static BusinessObjectChoiceOptionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
