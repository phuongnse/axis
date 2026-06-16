namespace Axis.Identity.Application.Services;

/// <summary>Removes platform-level Identity records for a hard-deleted Workspace.</summary>
public interface IWorkspaceIdentityPurger
{
    Task PurgeAsync(Guid workspaceId, string? logoUrl, CancellationToken cancellationToken = default);
}
