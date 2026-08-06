using Axis.Audit.Application.Persistence;
using Axis.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Axis.Audit.Infrastructure.Persistence;

internal sealed class AuditUnitOfWork(AuditDbContext context) : IAuditUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            foreach (EntityEntry<AuditRecord>? entry in context.ChangeTracker.Entries<AuditRecord>().Where(entry => entry.State == EntityState.Added))
                entry.State = EntityState.Detached;
            throw new AuditRecordAlreadyExistsException(exception);
        }
    }
}
