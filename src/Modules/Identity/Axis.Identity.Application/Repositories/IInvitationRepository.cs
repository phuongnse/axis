using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IInvitationRepository
{
    Task AddAsync(Invitation invitation, CancellationToken ct = default);
    Task<Invitation?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task<Invitation?> GetPendingByEmailAsync(Email email, Guid organizationId, CancellationToken ct = default);

    Task<int> CountPendingAsync(Guid organizationId, CancellationToken ct = default);
}
