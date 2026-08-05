using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.DeactivateRuleDefinition;

public sealed record DeactivateRuleDefinitionCommand(string DefinitionKey, int ExpectedRevision)
    : ICommand<RuleDefinitionDetailDto>;
