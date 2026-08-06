using Axis.Identity.Domain.Aggregates;
namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceMembershipRepository { Task AddAsync(WorkspaceMembership membership, CancellationToken ct = default); Task<WorkspaceMembership?> GetActiveAsync(Guid workspaceId, Guid userId, CancellationToken ct = default); Task<IReadOnlyList<WorkspaceMembership>> ListActiveForUserAsync(Guid userId, CancellationToken ct = default); }
