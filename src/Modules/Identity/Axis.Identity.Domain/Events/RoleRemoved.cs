using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record RoleRemoved(Guid UserId, Guid tenantId, Guid RoleId) : IDomainEvent;
