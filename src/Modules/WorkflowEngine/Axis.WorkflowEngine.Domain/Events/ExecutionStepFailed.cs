using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionStepFailed(
    Guid ExecutionId,
    Guid StepId,
    Guid tenantId,
    string ErrorDetails) : IDomainEvent;
