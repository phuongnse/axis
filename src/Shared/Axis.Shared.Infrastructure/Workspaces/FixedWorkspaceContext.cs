using Axis.Shared.Application.Workspaces;

namespace Axis.Shared.Infrastructure.Workspaces;

/// <summary>
/// Resolves a fixed Workspace for background jobs (e.g. workspace provisioning,
/// Wolverine handlers consuming cross-module events outside an HTTP context).
/// </summary>
public sealed class FixedWorkspaceContext(Guid workspaceId) : IWorkspaceContext
{
    public Guid workspaceId { get; } = workspaceId;
    public string SchemaName => $"workspace_{workspaceId:N}";
}
