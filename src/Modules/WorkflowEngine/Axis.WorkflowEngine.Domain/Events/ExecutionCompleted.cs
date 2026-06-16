using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionCompleted(Guid ExecutionId, Guid tenantId) : IDomainEvent;
