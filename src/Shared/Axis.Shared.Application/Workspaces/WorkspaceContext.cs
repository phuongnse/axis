namespace Axis.Shared.Application.Workspaces;

public sealed class WorkspaceContext(Guid workspaceId)
{
    public Guid workspaceId { get; } = workspaceId;

    /// <summary>
    /// PostgreSQL schema name for this workspace. Uses Workspace ID (N format) so the
    /// schema name is stable regardless of Workspace slug changes.
    /// </summary>
    public string SchemaName => $"workspace_{workspaceId:N}";
}
