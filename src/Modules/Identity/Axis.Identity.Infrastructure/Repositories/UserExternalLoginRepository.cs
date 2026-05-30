using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class UserExternalLoginRepository(IdentityDbContext context) : IUserExternalLoginRepository
{
    public async Task AddAsync(UserExternalLogin login, CancellationToken ct = default) =>
        await context.UserExternalLogins.AddAsync(login, ct);

    public async Task<bool> ExistsAsync(
        ExternalIdentityProvider provider,
        string providerKey,
        CancellationToken ct = default) =>
        await context.UserExternalLogins.AnyAsync(
            l => l.Provider == provider && l.ProviderKey == providerKey,
            ct);
}
