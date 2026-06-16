using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record UserProfileUpdated(
    Guid UserId,
    Guid tenantId,
    string FullName,
    string? AvatarUrl) : IDomainEvent;
