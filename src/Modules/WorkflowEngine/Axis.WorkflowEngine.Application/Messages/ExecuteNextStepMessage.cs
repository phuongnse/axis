namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched after each step completes (or at execution start) to advance
/// the execution to its next pending step. Processed by ExecuteNextStepHandler.
/// </summary>
public sealed record ExecuteNextStepMessage(
    Guid ExecutionId,
    Guid TeamAccountId);
