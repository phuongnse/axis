using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class ExternalRegistrationSessionRepository(IdentityDbContext context)
    : IExternalRegistrationSessionRepository
{
    public async Task AddAsync(ExternalRegistrationSession session, CancellationToken ct = default) =>
        await context.ExternalRegistrationSessions.AddAsync(session, ct);

    public async Task<ExternalRegistrationSession?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await context.ExternalRegistrationSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
}
