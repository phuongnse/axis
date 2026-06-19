using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Services;

internal static class WorkspaceRoleCatalog
{
    private static readonly string[] AdminPermissions =
    [
        "data_modeling:model:read",
        "data_modeling:model:write",
        "data_modeling:model:delete",
        "data_modeling:record:read",
        "data_modeling:record:write",
        "data_modeling:record:delete",
        "workflow:definition:read",
        "workflow:definition:write",
        "workflow:definition:delete",
        "workflow:trigger:manual",
        "form:definition:read",
        "form:definition:write",
        "form:submit",
        "execution:read",
        "execution:cancel",
        "execution:retry",
        "page:read",
        "page:write",
        "page:publish",
        "users:read",
        "users:invite",
        "users:deactivate",
        "roles:read",
        "roles:write",
        "workspace:settings:read",
        "workspace:settings:write",
        "workspace:delete"
    ];

    private static readonly string[] EditorPermissions =
    [
        "data_modeling:model:read",
        "data_modeling:model:write",
        "data_modeling:record:read",
        "data_modeling:record:write",
        "workflow:definition:read",
        "workflow:definition:write",
        "workflow:trigger:manual",
        "form:definition:read",
        "form:definition:write",
        "execution:read",
        "execution:cancel",
        "execution:retry",
        "page:read",
        "page:write"
    ];

    private static readonly string[] ViewerPermissions =
    [
        "data_modeling:model:read",
        "data_modeling:record:read",
        "workflow:definition:read",
        "form:definition:read",
        "execution:read",
        "page:read"
    ];

    private static readonly string[] EndUserPermissions =
    [
        "form:submit"
    ];

    public static IReadOnlyList<Role> CreateDefaultRoles(Guid workspaceId) =>
    [
        Role.CreateSystem("Admin", workspaceId, AdminPermissions),
        Role.CreateSystem("Editor", workspaceId, EditorPermissions),
        Role.CreateSystem("Viewer", workspaceId, ViewerPermissions),
        Role.CreateSystem("End User", workspaceId, EndUserPermissions)
    ];
}
