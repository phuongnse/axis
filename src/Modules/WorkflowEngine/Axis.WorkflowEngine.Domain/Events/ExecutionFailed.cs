using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionFailed(Guid ExecutionId, Guid OrganizationId, string ErrorMessage) : IDomainEvent;
