using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;

public sealed record SaveRuleDefinitionDraftCommand(
    string DefinitionKey,
    int ExpectedRevision,
    string Name,
    string Description,
    IReadOnlyList<RuleDraftInputDefinitionDto> Inputs,
    RuleConditionNodeDto Condition)
    : ICommand<RuleDefinitionDetailDto>;
