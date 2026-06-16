using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

public sealed record WorkflowCreated(Guid WorkflowId, Guid TeamAccountId, string Name) : IDomainEvent;
