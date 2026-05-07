using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

public sealed record WorkflowCreated(Guid WorkflowId, Guid OrganizationId, string Name) : IDomainEvent;
public sealed record WorkflowPublished(Guid WorkflowId, Guid OrganizationId) : IDomainEvent;
public sealed record WorkflowArchived(Guid WorkflowId, Guid OrganizationId) : IDomainEvent;
