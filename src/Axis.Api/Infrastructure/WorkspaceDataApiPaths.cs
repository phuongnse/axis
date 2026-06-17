namespace Axis.Api.Infrastructure;

/// <summary>
/// Routes that use per-workspace PostgreSQL schemas.
/// </summary>
internal static class WorkspaceDataApiPaths
{
    private static readonly string[] Prefixes =
    [
        "/api/models",
        "/api/data-classes",
        "/api/workflows",
        "/api/executions",
        "/api/forms",
        "/api/form-tasks/mine",
    ];

    public static bool RequiresWorkspaceDataAccess(PathString path)
    {
        foreach (string prefix in Prefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
