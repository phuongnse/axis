namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>
/// Dispatched by ExecuteNextStepHandler when the next step to run is a Condition step.
/// Carries the snapshot of all step IDs so the handler can skip non-selected branches.
/// </summary>
public sealed record ExecuteConditionStepMessage(
    Guid ExecutionId,
    Guid StepId,
    Guid TeamAccountId,
    IReadOnlyDictionary<string, object?>? StepConfig,
    IReadOnlyDictionary<string, object?> Context,
    IReadOnlyList<Guid> AllStepDefinitionIds,
    IReadOnlyList<ConditionTransition> Transitions);


