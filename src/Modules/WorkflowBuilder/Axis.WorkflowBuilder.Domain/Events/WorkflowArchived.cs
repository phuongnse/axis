using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

public sealed record WorkflowArchived(Guid WorkflowId, Guid tenantId) : IDomainEvent;
