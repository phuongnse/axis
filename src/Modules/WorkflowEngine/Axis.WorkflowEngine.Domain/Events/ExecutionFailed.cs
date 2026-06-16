using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionFailed(Guid ExecutionId, Guid TeamAccountId, string ErrorMessage) : IDomainEvent;
