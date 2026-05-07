namespace Axis.WorkflowBuilder.Domain.ValueObjects;

/// <summary>Represents a directed edge between two steps in a workflow.</summary>
public sealed record StepTransition(Guid FromStepId, Guid ToStepId, string? Label);
