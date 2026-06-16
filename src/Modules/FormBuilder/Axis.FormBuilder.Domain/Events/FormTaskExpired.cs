using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskExpired(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid tenantId,
    Guid ExecutionId,
    Guid ExecutionStepId) : IDomainEvent;
