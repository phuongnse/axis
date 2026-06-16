using axis.identity.events;
using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Infrastructure.Messaging;

/// <summary>Maps Identity domain events to Avro contract messages for Kafka (ADR-019).</summary>
internal static class IdentityEventMapper
{
    public static object? ToIntegrationEvent(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            TenantVerified verified => new TenantVerifiedEvent
            {
                tenantId = verified.tenantId.ToString(),
            },
            UserDeactivated deactivated => new UserDeactivatedEvent
            {
                userId = deactivated.UserId.ToString(),
                tenantId = deactivated.tenantId.ToString(),
            },
            UserReactivated reactivated => new UserReactivatedEvent
            {
                userId = reactivated.UserId.ToString(),
                tenantId = reactivated.tenantId.ToString(),
            },
            RoleAssigned assigned => new RoleAssignedEvent
            {
                userId = assigned.UserId.ToString(),
                tenantId = assigned.tenantId.ToString(),
                roleId = assigned.RoleId.ToString(),
            },
            RoleRemoved removed => new RoleRemovedEvent
            {
                userId = removed.UserId.ToString(),
                tenantId = removed.tenantId.ToString(),
                roleId = removed.RoleId.ToString(),
            },
            _ => null,
        };
}
