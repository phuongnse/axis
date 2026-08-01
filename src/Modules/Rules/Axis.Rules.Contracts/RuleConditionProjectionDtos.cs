namespace Axis.Rules.Contracts;

public enum RuleExpressionReferenceKind
{
    LogicalOperator,
    PredicateOperator,
    Function,
    Input,
    Literal,
    ValueType,
    OperandKind,
    Limit,
}

public sealed record RuleExpressionDisplayTokenDto(
    string Text,
    RuleExpressionReferenceKind? ReferenceKind = null,
    string? ReferenceKey = null,
    bool IsCode = false);

public sealed record RuleExpressionDisplayNodeDto(
    string NodeId,
    IReadOnlyList<RuleExpressionDisplayTokenDto> Tokens,
    IReadOnlyList<RuleExpressionDisplayNodeDto> Children);

public sealed record RuleConditionProjectionDto(
    RuleConditionNodeDto Condition,
    RuleExpressionDisplayNodeDto Display);
