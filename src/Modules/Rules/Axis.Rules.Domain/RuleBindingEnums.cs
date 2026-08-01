namespace Axis.Rules.Domain;

public enum RuleInputMappingKind
{
    Context = 0,
    Literal = 1,
}

public enum RuleBindingFailureBehavior
{
    FailClosed = 0,
    FailOpen = 1,
}
