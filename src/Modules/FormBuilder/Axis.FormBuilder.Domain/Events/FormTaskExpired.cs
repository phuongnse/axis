using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskExpired(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid workspaceId,
    Guid ExecutionId,
    Guid ExecutionStepId) : IDomainEvent;
