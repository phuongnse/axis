using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionStarted(Guid ExecutionId, Guid WorkflowDefinitionId, Guid OrganizationId) : IDomainEvent;
public sealed record ExecutionCompleted(Guid ExecutionId, Guid OrganizationId) : IDomainEvent;
public sealed record ExecutionFailed(Guid ExecutionId, Guid OrganizationId, string ErrorMessage) : IDomainEvent;
public sealed record ExecutionCancelled(Guid ExecutionId, Guid OrganizationId) : IDomainEvent;
