namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by ExecuteNextStepHandler when the next step to run is a Notification step.
/// </summary>
public sealed record ExecuteNotificationStepMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid TeamAccountId,
    IReadOnlyDictionary<string, object?>? StepConfig,
    IReadOnlyDictionary<string, object?> Context);
