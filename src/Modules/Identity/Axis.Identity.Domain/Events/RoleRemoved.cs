using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record RoleRemoved(Guid UserId, Guid TeamAccountId, Guid RoleId) : IDomainEvent;
