using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveStep;

public sealed record RemoveStepCommand(
    Guid WorkflowId,
    Guid OrganizationId,
    Guid StepId) : ICommand;
