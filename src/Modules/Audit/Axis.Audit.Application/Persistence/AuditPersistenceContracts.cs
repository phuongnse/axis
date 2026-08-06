using Axis.Audit.Domain;

namespace Axis.Audit.Application.Persistence;

public interface IAuditRecordRepository
{
    Task AddAsync(AuditRecord record, CancellationToken cancellationToken = default);
    Task<AuditRecord?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public interface IAuditUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class AuditRecordAlreadyExistsException : Exception
{
    public AuditRecordAlreadyExistsException(Exception innerException) : base("Audit record already exists.", innerException)
    {
    }
}
