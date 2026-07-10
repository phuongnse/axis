namespace Axis.Rules.Domain;

public readonly record struct RuleDefinitionId(Guid Value)
{
    public static RuleDefinitionId New() => new(Guid.NewGuid());
    public static RuleDefinitionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
