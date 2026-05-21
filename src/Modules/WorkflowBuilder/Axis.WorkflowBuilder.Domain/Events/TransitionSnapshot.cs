namespace Axis.WorkflowBuilder.Domain.Events;

/// <summary>Immutable snapshot of a transition (directed edge) at publish time.</summary>
public sealed record TransitionSnapshot(
    Guid FromStepId,
    Guid ToStepId,
    string? Label);
