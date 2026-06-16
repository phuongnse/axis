using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record RoleUpdated(
    Guid RoleId,
    Guid tenantId,
    string Name) : IDomainEvent;
