using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IOrganizationMembershipRepository
{
    Task AddAsync(OrganizationMembership membership, CancellationToken ct = default);
    Task<OrganizationMembership?> GetByUserAndOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
    Task<OrganizationMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationMembership>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountActiveUsersAsync(Guid organizationId, CancellationToken ct = default);
    Task<int> CountAdminsAsync(Guid organizationId, Guid adminRoleId, CancellationToken ct = default);
}
