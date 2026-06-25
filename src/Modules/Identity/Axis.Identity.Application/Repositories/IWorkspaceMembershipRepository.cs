using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceMembershipRepository
{
    Task AddAsync(WorkspaceMembership membership, CancellationToken ct = default);
    Task<WorkspaceMembership?> GetByUserAndWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct = default);
    Task<WorkspaceMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceMembership>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountActiveUsersAsync(Guid workspaceId, CancellationToken ct = default);
}
