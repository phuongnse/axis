using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormTaskCreated(
    Guid FormSubmissionId,
    Guid FormDefinitionId,
    Guid OrganizationId,
    Guid ExecutionId,
    Guid? AssigneeUserId,
    Guid? AssigneeRoleId,
    DateTimeOffset? ExpiresAt,
    Guid AccessToken) : IDomainEvent;
