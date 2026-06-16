namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by ExecuteNextStepHandler when the next step to run is a Form step.
/// The handler creates a FormSubmission task and suspends the step in WAITING state.
/// </summary>
public sealed record ExecuteFormStepMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid TeamAccountId,
    Guid WorkflowDefinitionId,
    IReadOnlyDictionary<string, object?>? StepConfig,
    IReadOnlyDictionary<string, object?> Context);
