using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.CreateRuleDefinitionVersion;

public sealed record CreateRuleDefinitionVersionCommand(string DefinitionKey, int ExpectedRevision)
    : ICommand<RuleDefinitionDetailDto>;
