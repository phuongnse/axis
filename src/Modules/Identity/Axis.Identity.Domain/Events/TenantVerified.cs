using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record TenantVerified(Guid tenantId) : IDomainEvent;
