namespace Axis.Rules.Contracts;

public enum RuleOrigin
{
    System = 0,
    Workspace = 1,
}

public enum RuleScope
{
    Field = 0,
    Object = 1,
    Record = 2,
    Lifecycle = 3,
}

public enum RuleOutcomeKind
{
    Validation = 0,
    Decision = 1,
}

public enum RuleLifecycleStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public enum RuleValueType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Date = 3,
    DateTime = 4,
    Boolean = 5,
}

public enum RuleSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public enum RuleDecision
{
    Allow = 0,
    Deny = 1,
}

public enum RuleLogicalOperator
{
    All = 0,
    Any = 1,
    Not = 2,
}

public enum RulePredicateOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    Contains = 6,
    StartsWith = 7,
    EndsWith = 8,
    IsNull = 9,
    IsNotNull = 10,
}

public enum RuleOperandKind
{
    Context = 0,
    Parameter = 1,
    Literal = 2,
    Function = 3,
}

public enum RuleExpressionFunction
{
    IsBlank = 0,
    Length = 1,
    Precision = 2,
    Scale = 3,
    Count = 4,
    MatchesPattern = 5,
    HasFormat = 6,
    ToDecimal = 7,
}

public enum RuleExpressionCardinality
{
    Scalar = 0,
    Multiple = 1,
    Any = 2,
}
