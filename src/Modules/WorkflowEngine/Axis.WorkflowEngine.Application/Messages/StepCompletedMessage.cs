namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by a step handler when the step completes successfully.
/// Processed by StepCompletedHandler, which updates state and advances the execution.
/// </summary>
public sealed record StepCompletedMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid tenantId,
    IReadOnlyDictionary<string, object?> Output);
