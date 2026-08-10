using Axis.Audit.Application;
using Axis.Audit.Contracts;

namespace Axis.Audit.Infrastructure;

internal sealed class AuditEventSink(IAuditEventIngestionService ingestionService) : IAuditEventSink
{
    public Task<AuditIngestionResult> IngestAsync(AuditEventV1 auditEvent, CancellationToken cancellationToken = default) =>
        ingestionService.IngestAsync(auditEvent, cancellationToken);

    public Task<AuditEventReadBackV1?> ReadBackAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        ingestionService.ReadBackAsync(eventId, cancellationToken);
}
