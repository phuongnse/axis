using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.UpdateRuleBinding;

public sealed record UpdateRuleBindingCommand(Guid BindingId, UpdateRuleBindingRequest Request)
    : ICommand<RuleBindingDto>;
