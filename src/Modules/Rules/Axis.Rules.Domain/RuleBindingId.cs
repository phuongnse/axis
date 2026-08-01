namespace Axis.Rules.Domain;

public readonly record struct RuleBindingId(Guid Value)
{
    public static RuleBindingId New() => new(Guid.NewGuid());
    public static RuleBindingId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
