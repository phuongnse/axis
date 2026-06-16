namespace Axis.Shared.Application.Workspaces;

/// <summary>
/// Interface for accessing workspace context within the application layer.
/// Implemented by infrastructure and injected into handlers.
/// </summary>
public interface IWorkspaceContext
{
    Guid workspaceId { get; }
    string SchemaName { get; }
}
