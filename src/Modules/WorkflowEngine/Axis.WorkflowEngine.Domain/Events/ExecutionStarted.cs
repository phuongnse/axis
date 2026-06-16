using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionStarted(Guid ExecutionId, Guid WorkflowDefinitionId, Guid tenantId) : IDomainEvent;
