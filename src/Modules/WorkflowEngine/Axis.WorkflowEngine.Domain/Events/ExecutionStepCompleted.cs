using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionStepCompleted(
    Guid ExecutionId,
    Guid StepId,
    Guid OrganizationId,
    IReadOnlyDictionary<string, object?> Output) : IDomainEvent;
