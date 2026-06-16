using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class InvitationRepository(IdentityDbContext context) : IInvitationRepository
{
    public async Task AddAsync(Invitation invitation, CancellationToken ct = default) =>
        await context.Invitations.AddAsync(invitation, ct);

    public async Task<Invitation?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        await context.Invitations.FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task<Invitation?> GetPendingByEmailAsync(Email email, Guid organizationId, CancellationToken ct = default) =>
        await context.Invitations
            .FirstOrDefaultAsync(i => i.Email == email
                                   && i.OrganizationId == organizationId
                                   && i.Status == InvitationStatus.Pending, ct);

    public Task<int> CountPendingAsync(Guid organizationId, CancellationToken ct = default) =>
        context.Invitations.CountAsync(
            i => i.OrganizationId == organizationId && i.Status == InvitationStatus.Pending,
            ct);
}
