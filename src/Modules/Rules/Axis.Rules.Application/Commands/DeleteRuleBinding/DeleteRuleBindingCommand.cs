using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Commands.DeleteRuleBinding;

public sealed record DeleteRuleBindingCommand(Guid BindingId, int ExpectedRevision) : ICommand;
