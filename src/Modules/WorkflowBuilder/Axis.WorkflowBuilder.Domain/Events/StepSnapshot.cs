namespace Axis.WorkflowBuilder.Domain.Events;

/// <summary>Immutable snapshot of a single step definition at publish time.</summary>
public sealed record StepSnapshot(
    Guid Id,
    string Name,
    string StepType,
    int DisplayOrder,
    IReadOnlyDictionary<string, object?>? Config);
