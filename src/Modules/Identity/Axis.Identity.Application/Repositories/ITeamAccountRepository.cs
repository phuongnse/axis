using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface ITeamAccountRepository
{
    Task AddAsync(TeamAccount teamAccount, CancellationToken ct = default);
    Task<TeamAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TeamAccount?> GetBySlugAsync(TeamAccountSlug slug, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(TeamAccountSlug slug, CancellationToken ct = default);
}
