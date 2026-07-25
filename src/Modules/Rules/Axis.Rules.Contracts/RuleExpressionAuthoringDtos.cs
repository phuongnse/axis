namespace Axis.Rules.Contracts;

public enum RuleExpressionReferenceKind
{
    LogicalOperator,
    PredicateOperator,
    Function,
    Context,
    Parameter,
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

public sealed record RuleExpressionDiagnosticDto(
    string Code,
    string Message,
    int Start,
    int Length);

public sealed record RuleExpressionCompletionDto(
    string Label,
    string InsertText,
    int CursorOffset,
    int ReplacementStart,
    int ReplacementLength,
    RuleExpressionReferenceKind ReferenceKind,
    string ReferenceKey,
    string Summary);

public sealed record RuleExpressionAuthoringDto(
    string Syntax,
    RuleConditionNodeDto? Condition,
    RuleExpressionDisplayNodeDto? Display,
    IReadOnlyList<RuleExpressionDiagnosticDto> Diagnostics,
    IReadOnlyList<RuleExpressionCompletionDto> Completions);
