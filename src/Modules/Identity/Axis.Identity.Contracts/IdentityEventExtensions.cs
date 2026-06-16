using axis.identity.events;

namespace Axis.Identity.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class IdentityEventExtensions
{
    public static Guid OrganizationId(this OrganizationVerifiedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid OrganizationId(this TenantModuleProvisionReportEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid UserId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid OrganizationId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid UserId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid OrganizationId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid UserId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid OrganizationId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid RoleId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    public static Guid UserId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid OrganizationId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid RoleId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
