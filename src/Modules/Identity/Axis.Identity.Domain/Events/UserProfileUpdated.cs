using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record UserProfileUpdated(
    Guid UserId,
    Guid TeamAccountId,
    string FullName,
    string? AvatarUrl) : IDomainEvent;
