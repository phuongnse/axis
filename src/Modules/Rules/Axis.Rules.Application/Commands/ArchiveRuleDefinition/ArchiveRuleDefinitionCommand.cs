using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.ArchiveRuleDefinition;

public sealed record ArchiveRuleDefinitionCommand(string DefinitionKey, int ExpectedRevision)
    : ICommand<RuleDefinitionDetailDto>;
