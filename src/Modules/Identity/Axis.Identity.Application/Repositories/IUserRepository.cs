using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(Email email, Guid teamAccountId, CancellationToken ct = default);

    /// <summary>Platform-wide email lookup (for registration uniqueness check).</summary>
    Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default);

    /// <summary>Platform-wide user lookup by email — used for sign-in (no team account context yet).</summary>
    Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default);

    /// <summary>Platform-wide user lookup by id — used for email verification (no team account context yet).</summary>
    Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns count of users with Admin role in the team account.</summary>
    Task<int> CountAdminsAsync(Guid teamAccountId, Guid adminRoleId, CancellationToken ct = default);

    /// <summary>Active users in the team account.</summary>
    Task<int> CountActiveUsersAsync(Guid teamAccountId, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllByTeamAccountAsync(Guid teamAccountId, CancellationToken ct = default);
}
