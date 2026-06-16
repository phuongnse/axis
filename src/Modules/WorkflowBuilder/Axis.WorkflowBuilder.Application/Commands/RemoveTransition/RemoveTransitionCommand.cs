using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveTransition;

public sealed record RemoveTransitionCommand(
    Guid WorkflowId,
    Guid tenantId,
    Guid FromStepId,
    Guid ToStepId) : ICommand;
