namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by ExecuteNextStepHandler when the next step to run is a Script step.
/// </summary>
public sealed record ExecuteScriptStepMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid tenantId,
    IReadOnlyDictionary<string, object?>? StepConfig,
    IReadOnlyDictionary<string, object?> Context);
