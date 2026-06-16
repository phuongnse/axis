using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

public sealed record ExecutionStepCompleted(
    Guid ExecutionId,
    Guid StepId,
    Guid TeamAccountId,
    IReadOnlyDictionary<string, object?> Output) : IDomainEvent;
