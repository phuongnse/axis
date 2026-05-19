using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

/// <param name="ReferencedFormIds">Form IDs referenced by Form steps in this workflow at publish time.</param>
public sealed record WorkflowPublished(
    Guid WorkflowId,
    Guid OrganizationId,
    IReadOnlyList<Guid> ReferencedFormIds) : IDomainEvent;
