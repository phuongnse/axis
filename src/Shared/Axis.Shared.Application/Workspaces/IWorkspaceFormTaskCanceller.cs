namespace Axis.Shared.Application.Workspaces;

/// <summary>Cancels pending form tasks before Workspace hard-delete.</summary>
public interface IWorkspaceFormTaskCanceller
{
    Task CancelPendingForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
