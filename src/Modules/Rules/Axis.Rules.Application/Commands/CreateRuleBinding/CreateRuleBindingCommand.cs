using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.CreateRuleBinding;

public sealed record CreateRuleBindingCommand(CreateRuleBindingRequest Request)
    : ICommand<RuleBindingDto>;
