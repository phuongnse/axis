using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TeamAccountRepository(IdentityDbContext context) : ITeamAccountRepository
{
    public async Task AddAsync(TeamAccount teamAccount, CancellationToken ct = default) =>
        await context.TeamAccounts.AddAsync(teamAccount, ct);

    public async Task<TeamAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await context.TeamAccounts.FindAsync([id], ct);

    public async Task<TeamAccount?> GetBySlugAsync(TeamAccountSlug slug, CancellationToken ct = default) =>
        await context.TeamAccounts
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public async Task<bool> SlugExistsAsync(TeamAccountSlug slug, CancellationToken ct = default) =>
        await context.TeamAccounts.AnyAsync(o => o.Slug == slug, ct);
}
