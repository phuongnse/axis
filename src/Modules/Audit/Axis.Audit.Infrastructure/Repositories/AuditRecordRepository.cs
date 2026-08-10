using Axis.Audit.Application.Persistence;
using Axis.Audit.Domain;
using Axis.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Audit.Infrastructure.Repositories;

internal sealed class AuditRecordRepository(AuditDbContext context) : IAuditRecordRepository
{
    public async Task AddAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
        await context.AuditRecords.AddAsync(record, cancellationToken);

    public Task<AuditRecord?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        context.AuditRecords.AsNoTracking().FirstOrDefaultAsync(record => record.EventId == eventId, cancellationToken);
}
