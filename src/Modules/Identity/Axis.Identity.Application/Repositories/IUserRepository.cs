using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(Email email, Guid tenantId, CancellationToken ct = default);

    /// <summary>Platform-wide email lookup (for registration uniqueness check).</summary>
    Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default);

    /// <summary>Platform-wide user lookup by email — used for sign-in (no Tenant context yet).</summary>
    Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default);

    /// <summary>Platform-wide user lookup by id — used for email verification (no Tenant context yet).</summary>
    Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns count of users with Admin role in the Tenant.</summary>
    Task<int> CountAdminsAsync(Guid tenantId, Guid adminRoleId, CancellationToken ct = default);

    /// <summary>Active users in the Tenant.</summary>
    Task<int> CountActiveUsersAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
