using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.CreateRuleDefinition;

public sealed record CreateRuleDefinitionCommand(
    string Name,
    string Description,
    RuleScope Scope,
    string ContextKey,
    int ContextSchemaVersion,
    RuleOutcomeKind OutcomeKind)
    : ICommand<RuleDefinitionDetailDto>;
