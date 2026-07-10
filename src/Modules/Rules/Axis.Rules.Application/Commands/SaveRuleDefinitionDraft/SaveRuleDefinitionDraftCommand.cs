using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;

public sealed record SaveRuleDefinitionDraftCommand(
    string DefinitionKey,
    int ExpectedRevision,
    string Name,
    string Description,
    RuleScope Scope,
    string ContextKey,
    int ContextSchemaVersion,
    RuleOutcomeKind OutcomeKind,
    IReadOnlyList<RuleParameterDefinitionDto> Parameters,
    RuleConditionNodeDto Condition,
    RuleOutcomeDto Outcome)
    : ICommand<RuleDefinitionDetailDto>;
