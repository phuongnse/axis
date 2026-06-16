using axis.identity.events;

namespace Axis.Identity.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class IdentityEventExtensions
{
    public static Guid TeamAccountId(this TeamAccountVerifiedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid TeamAccountId(this TenantModuleProvisionReportEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid UserId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid TeamAccountId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid UserId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid TeamAccountId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid UserId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid TeamAccountId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid RoleId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    public static Guid UserId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid TeamAccountId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid RoleId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
