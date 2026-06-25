using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct = default);

    /// <summary>Platform-wide email lookup (for registration uniqueness check).</summary>
    Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default);

    Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default);

    Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default);
}
