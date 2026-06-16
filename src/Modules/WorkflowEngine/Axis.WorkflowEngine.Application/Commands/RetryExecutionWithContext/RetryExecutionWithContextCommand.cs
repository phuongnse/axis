using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowEngine.Application.Commands.RetryExecutionWithContext;

/// <summary>Retry a failed execution using a user-supplied modified context.</summary>
public sealed record RetryExecutionWithContextCommand(
    Guid ExecutionId,
    Guid workspaceId,
    Guid? RetriedByUserId,
    IReadOnlyDictionary<string, object?> ModifiedContext) : ICommand<Guid>;
