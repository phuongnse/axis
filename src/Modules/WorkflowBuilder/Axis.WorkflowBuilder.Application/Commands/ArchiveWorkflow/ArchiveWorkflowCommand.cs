using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.ArchiveWorkflow;

public sealed record ArchiveWorkflowCommand(Guid WorkflowId, Guid workspaceId) : ICommand;
