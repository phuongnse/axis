using axis.identity.events;

namespace Axis.Identity.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class IdentityEventExtensions
{
    public static Guid tenantId(this TenantVerifiedEvent @event)
        => ParseRequiredGuid(@event.tenantId, nameof(@event.tenantId));

    public static Guid tenantId(this TenantModuleProvisionReportEvent @event)
        => ParseRequiredGuid(@event.tenantId, nameof(@event.tenantId));

    public static Guid UserId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid tenantId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.tenantId, nameof(@event.tenantId));

    public static Guid UserId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid tenantId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.tenantId, nameof(@event.tenantId));

    public static Guid UserId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid tenantId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.tenantId, nameof(@event.tenantId));

    public static Guid RoleId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    public static Guid UserId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid tenantId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.tenantId, nameof(@event.tenantId));

    public static Guid RoleId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
