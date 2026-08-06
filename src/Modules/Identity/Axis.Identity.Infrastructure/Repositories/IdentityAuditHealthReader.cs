using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class IdentityAuditHealthReader(IdentityDbContext context)
    : IIdentityAuditHealthReader
{
    public async Task<IdentityAuditHealthSnapshot> ReadAsync(CancellationToken ct = default)
    {
        int poisoned = await context.IdentityAuditOutboxRecords.CountAsync(
            record => record.Status == IdentityAuditOutboxStatus.Poisoned,
            ct);
        DateTimeOffset? oldestPending = await context.IdentityAuditOutboxRecords
            .Where(record => record.Status == IdentityAuditOutboxStatus.Pending)
            .MinAsync(record => (DateTimeOffset?)record.CreatedAt, ct);
        return new IdentityAuditHealthSnapshot(poisoned, oldestPending);
    }
}
