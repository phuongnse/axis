using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record RoleAssigned(Guid UserId, Guid TeamAccountId, Guid RoleId) : IDomainEvent;
