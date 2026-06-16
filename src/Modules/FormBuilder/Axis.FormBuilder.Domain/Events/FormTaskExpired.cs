using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskExpired(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid TeamAccountId,
    Guid ExecutionId,
    Guid ExecutionStepId) : IDomainEvent;
