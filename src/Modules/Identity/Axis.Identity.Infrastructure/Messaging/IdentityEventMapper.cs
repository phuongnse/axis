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
            TeamAccountVerified verified => new TeamAccountVerifiedEvent
            {
                teamAccountId = verified.TeamAccountId.ToString(),
            },
            UserDeactivated deactivated => new UserDeactivatedEvent
            {
                userId = deactivated.UserId.ToString(),
                teamAccountId = deactivated.TeamAccountId.ToString(),
            },
            UserReactivated reactivated => new UserReactivatedEvent
            {
                userId = reactivated.UserId.ToString(),
                teamAccountId = reactivated.TeamAccountId.ToString(),
            },
            RoleAssigned assigned => new RoleAssignedEvent
            {
                userId = assigned.UserId.ToString(),
                teamAccountId = assigned.TeamAccountId.ToString(),
                roleId = assigned.RoleId.ToString(),
            },
            RoleRemoved removed => new RoleRemovedEvent
            {
                userId = removed.UserId.ToString(),
                teamAccountId = removed.TeamAccountId.ToString(),
                roleId = removed.RoleId.ToString(),
            },
            _ => null,
        };
}
