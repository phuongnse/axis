using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(Email email, Guid organizationId, CancellationToken ct = default);

    /// <summary>Platform-wide email lookup (for registration uniqueness check).</summary>
    Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default);

    /// <summary>Returns count of users with Admin role in the org.</summary>
    Task<int> CountAdminsAsync(Guid organizationId, Guid adminRoleId, CancellationToken ct = default);
}
