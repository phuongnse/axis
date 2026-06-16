using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record UserDeactivated(Guid UserId, Guid TeamAccountId) : IDomainEvent;
