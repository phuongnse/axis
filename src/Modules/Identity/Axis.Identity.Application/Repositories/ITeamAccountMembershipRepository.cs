using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface ITeamAccountMembershipRepository
{
    Task AddAsync(TeamAccountMembership membership, CancellationToken ct = default);
    Task<TeamAccountMembership?> GetByUserAndTeamAccountAsync(Guid userId, Guid teamAccountId, CancellationToken ct = default);
    Task<TeamAccountMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<TeamAccountMembership>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountActiveUsersAsync(Guid teamAccountId, CancellationToken ct = default);
    Task<int> CountAdminsAsync(Guid teamAccountId, Guid adminRoleId, CancellationToken ct = default);
}
