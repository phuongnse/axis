using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

/// <param name="ReferencedFormIds">Form IDs referenced by Form steps in this workflow, re-synced on unarchive.</param>
public sealed record WorkflowUnarchived(
    Guid WorkflowId,
    Guid tenantId,
    IReadOnlyList<Guid> ReferencedFormIds) : IDomainEvent;
