using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record UserRegistered(
    Guid UserId,
    string Email) : IDomainEvent;
