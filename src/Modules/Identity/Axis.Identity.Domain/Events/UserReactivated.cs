using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record UserReactivated(Guid UserId, Guid tenantId) : IDomainEvent;
