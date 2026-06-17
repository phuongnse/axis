using axis.identity.events;

namespace Axis.Identity.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class IdentityEventExtensions
{
    public static Guid workspaceId(this WorkspaceVerifiedEvent @event)
        => ParseRequiredGuid(@event.workspaceId, nameof(@event.workspaceId));

    public static Guid workspaceId(this WorkspaceModuleProvisionReportEvent @event)
        => ParseRequiredGuid(@event.workspaceId, nameof(@event.workspaceId));

    public static Guid UserId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid workspaceId(this UserDeactivatedEvent @event)
        => ParseRequiredGuid(@event.workspaceId, nameof(@event.workspaceId));

    public static Guid UserId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid workspaceId(this UserReactivatedEvent @event)
        => ParseRequiredGuid(@event.workspaceId, nameof(@event.workspaceId));

    public static Guid UserId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid workspaceId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.workspaceId, nameof(@event.workspaceId));

    public static Guid RoleId(this RoleAssignedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    public static Guid UserId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.userId, nameof(@event.userId));

    public static Guid workspaceId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.workspaceId, nameof(@event.workspaceId));

    public static Guid RoleId(this RoleRemovedEvent @event)
        => ParseRequiredGuid(@event.roleId, nameof(@event.roleId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
