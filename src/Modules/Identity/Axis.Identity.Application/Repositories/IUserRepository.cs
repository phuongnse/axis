using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(Email email, Guid workspaceId, CancellationToken ct = default);

    /// <summary>Platform-wide email lookup (for registration uniqueness check).</summary>
    Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default);

    /// <summary>Platform-wide user lookup by email — used for sign-in (no Workspace context yet).</summary>
    Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default);

    /// <summary>Platform-wide user lookup by id — used for email verification (no Workspace context yet).</summary>
    Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns count of users with Admin role in the Workspace.</summary>
    Task<int> CountAdminsAsync(Guid workspaceId, Guid adminRoleId, CancellationToken ct = default);

    /// <summary>Active users in the Workspace.</summary>
    Task<int> CountActiveUsersAsync(Guid workspaceId, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}
