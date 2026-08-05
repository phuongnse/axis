namespace Axis.Rules.Contracts;

public sealed record RuleAuthoringSourceDto(
    string? Text = null,
    RuleConditionNodeDto? Ast = null);

public sealed record RuleAuthoringDiagnosticDto(
    string Code,
    string Message,
    int Start,
    int Length);

public sealed record RuleAuthoringProjectionDto(
    RuleConditionNodeDto? Condition,
    string? FormattedDsl,
    RuleExpressionDisplayNodeDto? Explanation,
    IReadOnlyList<RuleAuthoringDiagnosticDto> Diagnostics)
{
    public bool IsValid => Condition is not null && Diagnostics.Count == 0;
}

public sealed record RuleAuthoringCompletionDto(
    string Label,
    string InsertText,
    string Kind,
    int Start,
    int Length);

public sealed record ProjectRuleAuthoringRequest(
    RuleAuthoringSourceDto Source,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    int ExpressionLanguageVersion,
    string? Language);

public sealed record CompleteRuleAuthoringRequest(
    string? Text,
    int Cursor,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    int ExpressionLanguageVersion);
