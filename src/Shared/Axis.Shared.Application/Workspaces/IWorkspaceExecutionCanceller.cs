namespace Axis.Shared.Application.Workspaces;

/// <summary>Cancels in-flight workflow executions before Workspace hard-delete.</summary>
public interface IWorkspaceExecutionCanceller
{
    Task CancelAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
