using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record TenantCreated(
    Guid tenantId,
    string Name,
    string Slug,
    string OwnerEmail) : IDomainEvent;
