using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IUserExternalLoginRepository
{
    Task AddAsync(UserExternalLogin login, CancellationToken ct = default);
    Task<bool> ExistsAsync(ExternalIdentityProvider provider, string providerKey, CancellationToken ct = default);
}
