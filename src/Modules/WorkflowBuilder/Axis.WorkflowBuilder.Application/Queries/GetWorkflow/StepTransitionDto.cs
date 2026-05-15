namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed record StepTransitionDto(
    Guid FromStepId,
    Guid ToStepId,
    string? Label);
