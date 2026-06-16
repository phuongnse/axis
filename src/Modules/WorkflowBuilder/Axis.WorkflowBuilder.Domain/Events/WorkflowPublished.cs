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
