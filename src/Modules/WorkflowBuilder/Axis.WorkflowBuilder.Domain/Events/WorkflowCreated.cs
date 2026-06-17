using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Events;

public sealed record WorkflowCreated(Guid WorkflowId, Guid workspaceId, string Name) : IDomainEvent;
