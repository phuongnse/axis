using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface ITenantMembershipRepository
{
    Task AddAsync(TenantMembership membership, CancellationToken ct = default);
    Task<TenantMembership?> GetByUserAndTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task<TenantMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantMembership>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountActiveUsersAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> CountAdminsAsync(Guid tenantId, Guid adminRoleId, CancellationToken ct = default);
}
