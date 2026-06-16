using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.UpdateWorkflow;

public sealed record UpdateWorkflowCommand(
    Guid WorkflowId,
    Guid workspaceId,
    string Name,
    string? Description) : ICommand;
