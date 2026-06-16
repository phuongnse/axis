namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by ExecuteNextStepHandler when the next step to run is an HTTP Request step.
/// </summary>
public sealed record ExecuteHttpStepMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid TeamAccountId,
    IReadOnlyDictionary<string, object?>? StepConfig,
    IReadOnlyDictionary<string, object?> Context);
