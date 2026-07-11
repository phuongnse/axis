namespace Axis.Rules.Domain;

public readonly record struct RuleDefinitionVersionId(Guid Value)
{
    public static RuleDefinitionVersionId New() => new(Guid.NewGuid());
    public static RuleDefinitionVersionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
