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

public sealed record ActivateRuleDefinitionVersionRequest(
    int Version,
    int ExpectedRevision);

public sealed record SimulateRuleDraftRequest(
    IReadOnlyDictionary<string, RuleValueDto> Inputs);

public sealed record SimulateRuleVersionRequest(
    IReadOnlyDictionary<string, RuleValueDto> Inputs);

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
