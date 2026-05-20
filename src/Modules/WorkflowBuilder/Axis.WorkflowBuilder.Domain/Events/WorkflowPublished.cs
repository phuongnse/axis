using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

/// <param name="ReferencedFormIds">Form IDs referenced by Form steps in this workflow at publish time.</param>
/// <param name="Steps">Snapshot of all step definitions at publish time. Consumers use this to build local read models.</param>
/// <param name="Transitions">Snapshot of all transitions at publish time.</param>
public sealed record WorkflowPublished(
    Guid WorkflowId,
    Guid OrganizationId,
    IReadOnlyList<Guid> ReferencedFormIds,
    IReadOnlyList<StepSnapshot> Steps,
    IReadOnlyList<TransitionSnapshot> Transitions) : IDomainEvent;

/// <summary>Immutable snapshot of a single step definition at publish time.</summary>
public sealed record StepSnapshot(
    Guid Id,
    string Name,
    string StepType,
    int DisplayOrder,
    IReadOnlyDictionary<string, object?>? Config);

/// <summary>Immutable snapshot of a transition (directed edge) at publish time.</summary>
public sealed record TransitionSnapshot(
    Guid FromStepId,
    Guid ToStepId,
    string? Label);
