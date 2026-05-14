using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

public sealed record WorkflowPublished(Guid WorkflowId, Guid OrganizationId) : IDomainEvent;
