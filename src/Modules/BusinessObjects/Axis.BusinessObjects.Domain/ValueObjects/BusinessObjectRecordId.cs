namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly record struct BusinessObjectRecordId(Guid Value)
{
    public static BusinessObjectRecordId New() => new(Guid.NewGuid());
    public static BusinessObjectRecordId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
