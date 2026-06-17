using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskCancelled(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid workspaceId,
    Guid ExecutionId) : IDomainEvent;
