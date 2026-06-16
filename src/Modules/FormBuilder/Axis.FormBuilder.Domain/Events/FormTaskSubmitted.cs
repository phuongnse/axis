using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskSubmitted(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid tenantId,
    Guid ExecutionId,
    Guid ExecutionStepId,
    IReadOnlyDictionary<string, object?> SubmittedData) : IDomainEvent;
