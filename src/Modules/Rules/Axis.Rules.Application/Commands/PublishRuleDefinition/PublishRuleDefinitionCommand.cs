using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.PublishRuleDefinition;

public sealed record PublishRuleDefinitionCommand(string DefinitionKey, int ExpectedRevision)
    : ICommand<RuleDefinitionDetailDto>;
