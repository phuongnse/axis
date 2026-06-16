using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowEngine.Application.Commands.RetryExecution;

/// <summary>Create a new retry execution for a failed execution.</summary>
public sealed record RetryExecutionCommand(
    Guid ExecutionId,
    Guid workspaceId,
    Guid? RetriedByUserId) : ICommand<Guid>;
