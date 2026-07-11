using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.StartRuleDefinitionDraft;

public sealed record StartRuleDefinitionDraftCommand(string DefinitionKey, int ExpectedRevision)
    : ICommand<RuleDefinitionDetailDto>;
