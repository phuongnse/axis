using Axis.Shared.Application.Workspaces;

namespace Axis.Shared.Infrastructure.Persistence;

/// <summary>
/// Resolves the <c>public</c> schema for <c>dotnet ef migrations</c> design-time only.
/// Runtime workspace modules use <see cref="Workspaces.FixedWorkspaceContext"/> or HTTP-scoped workspace context.
/// </summary>
public sealed class DesignTimePublicSchemaWorkspaceContext : IWorkspaceContext
{
    public Guid workspaceId => Guid.Empty;

    public string SchemaName => "public";
}
