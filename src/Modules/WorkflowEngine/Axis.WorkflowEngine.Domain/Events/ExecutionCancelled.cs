using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionCancelled(Guid ExecutionId, Guid TeamAccountId) : IDomainEvent;
