using Axis.Rules.Contracts;

namespace Axis.Rules.Application;

public sealed record CreateRuleDefinitionRequest(
    string Name,
    string Description);

public sealed record SaveRuleDefinitionDraftRequest(
    int ExpectedRevision,
    string Name,
    string Description,
    IReadOnlyList<RuleDraftInputDefinitionDto> Inputs,
    RuleConditionNodeDto Condition);

public sealed record RuleRevisionRequest(int ExpectedRevision);

public sealed record SimulateRuleRequest(
    int? DefinitionVersion,
    IReadOnlyDictionary<string, RuleValueDto> Inputs,
    string CorrelationId);

public sealed record RuleSimulationResultDto(
    string DefinitionKey,
    int? DefinitionVersion,
    bool IsMatch,
    IReadOnlyList<RuleNodeDiagnosticDto> Diagnostics,
    string CorrelationId);

public sealed record ProjectRuleConditionRequest(
    int ExpressionLanguageVersion,
    IReadOnlyList<RuleDraftInputDefinitionDto> Inputs,
    RuleConditionNodeDto Condition,
    string Language);
