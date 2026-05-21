namespace Axis.WorkflowEngine.Domain.ReadModels;

/// <summary>Immutable snapshot of a transition (directed edge) captured at workflow publish time.</summary>
public sealed class TransitionSnapshot
{
    public Guid FromStepId { get; init; }
    public Guid ToStepId { get; init; }
    public string? Label { get; init; }
}
