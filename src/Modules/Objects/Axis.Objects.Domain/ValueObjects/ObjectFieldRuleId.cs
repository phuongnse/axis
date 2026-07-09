namespace Axis.Objects.Domain.ValueObjects;

public readonly record struct ObjectFieldRuleId(Guid Value)
{
    public static ObjectFieldRuleId New() => new(Guid.NewGuid());
    public static ObjectFieldRuleId From(Guid value) => new(value);
}
