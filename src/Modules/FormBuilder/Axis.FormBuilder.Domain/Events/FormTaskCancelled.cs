using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskCancelled(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid tenantId,
    Guid ExecutionId) : IDomainEvent;
