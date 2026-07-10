using Axis.Rules.Contracts;

namespace Axis.Rules.Application;

public sealed record CreateRuleDefinitionRequest(
    string Name,
    string Description,
    RuleScope Scope,
    string ContextKey,
    int ContextSchemaVersion,
    RuleOutcomeKind OutcomeKind);

public sealed record SaveRuleDefinitionDraftRequest(
    int ExpectedRevision,
    string Name,
    string Description,
    RuleScope Scope,
    string ContextKey,
    int ContextSchemaVersion,
    RuleOutcomeKind OutcomeKind,
    IReadOnlyList<RuleParameterDefinitionDto> Parameters,
    RuleConditionNodeDto Condition,
    RuleOutcomeDto Outcome);

public sealed record RuleRevisionRequest(int ExpectedRevision);

public sealed record SimulateRuleRequest(
    int? DefinitionVersion,
    IReadOnlyDictionary<string, RuleValueDto> Parameters,
    IReadOnlyDictionary<string, RuleValueDto> Context,
    string CorrelationId);

public sealed record RuleSimulationResultDto(
    string DefinitionKey,
    int? DefinitionVersion,
    bool IsMatch,
    RuleOutcomeDto? Outcome,
    IReadOnlyList<RuleNodeDiagnosticDto> Diagnostics,
    string CorrelationId);

public sealed record RuleNodeDiagnosticDto(string NodeId, bool IsMatch);
