using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.ActivateRuleDefinitionVersion;

public sealed record ActivateRuleDefinitionVersionCommand(string DefinitionKey, int Version, int ExpectedRevision)
    : ICommand<RuleDefinitionDetailDto>;
