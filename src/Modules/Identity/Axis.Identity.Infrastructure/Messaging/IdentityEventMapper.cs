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
            WorkspaceVerified verified => new WorkspaceVerifiedEvent
            {
                workspaceId = verified.workspaceId.ToString(),
            },
            UserDeactivated deactivated => new UserDeactivatedEvent
            {
                userId = deactivated.UserId.ToString(),
                workspaceId = deactivated.workspaceId.ToString(),
            },
            UserReactivated reactivated => new UserReactivatedEvent
            {
                userId = reactivated.UserId.ToString(),
                workspaceId = reactivated.workspaceId.ToString(),
            },
            RoleAssigned assigned => new RoleAssignedEvent
            {
                userId = assigned.UserId.ToString(),
                workspaceId = assigned.workspaceId.ToString(),
                roleId = assigned.RoleId.ToString(),
            },
            RoleRemoved removed => new RoleRemovedEvent
            {
                userId = removed.UserId.ToString(),
                workspaceId = removed.workspaceId.ToString(),
                roleId = removed.RoleId.ToString(),
            },
            _ => null,
        };
}
