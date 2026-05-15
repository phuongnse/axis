using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.UpdateWorkflow;

public sealed record UpdateWorkflowCommand(
    Guid WorkflowId,
    Guid OrganizationId,
    string Name,
    string? Description) : ICommand;
