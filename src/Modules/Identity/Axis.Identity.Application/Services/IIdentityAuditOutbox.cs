using Axis.Audit.Contracts;

namespace Axis.Identity.Application.Services;

public interface IIdentityAuditOutbox
{
    Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default);
}
