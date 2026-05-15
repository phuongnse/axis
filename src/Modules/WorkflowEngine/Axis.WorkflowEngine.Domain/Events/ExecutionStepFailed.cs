using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionStepFailed(
    Guid ExecutionId,
    Guid StepId,
    Guid OrganizationId,
    string ErrorDetails) : IDomainEvent;
