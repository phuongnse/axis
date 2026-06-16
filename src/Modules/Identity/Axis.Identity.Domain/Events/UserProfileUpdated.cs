using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record UserProfileUpdated(
    Guid UserId,
    Guid OrganizationId,
    string FullName,
    string? AvatarUrl) : IDomainEvent;
