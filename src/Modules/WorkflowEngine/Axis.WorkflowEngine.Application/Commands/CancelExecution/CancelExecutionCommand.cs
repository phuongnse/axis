using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowEngine.Application.Commands.CancelExecution;

/// <summary>Cancel a running or pending execution.</summary>
public sealed record CancelExecutionCommand(Guid ExecutionId, Guid workspaceId) : ICommand;
