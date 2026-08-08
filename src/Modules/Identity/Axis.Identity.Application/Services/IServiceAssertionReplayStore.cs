using Axis.Audit.Contracts;

namespace Axis.Identity.Application.Services;

public interface IServiceAssertionReplayStore
{
    /// <returns>false when a valid-until digest was already accepted. Both
    /// outcomes persist their required audit event before returning.</returns>
    Task<bool> TryAcceptAsync(
        string digest,
        DateTime expiresAt,
        AuditEventV1 successAudit,
        AuditEventV1 replayAudit,
        CancellationToken ct = default);

    Task RecordAuditAsync(
        AuditEventV1 auditEvent,
        CancellationToken ct = default);

    Task PurgeExpiredAsync(DateTime now, CancellationToken ct = default);
}
