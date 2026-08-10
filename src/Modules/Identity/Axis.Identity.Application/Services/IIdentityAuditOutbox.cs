using Axis.Audit.Contracts;

namespace Axis.Identity.Application.Services;

public interface IIdentityAuditOutbox
{
    Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default);
    Task<IdentityAuditOutboxEntry?> GetAsync(Guid eventId, CancellationToken ct = default);
}

public sealed record IdentityAuditOutboxEntry(
    AuditEventV1 Event,
    IdentityAuditOutboxState State);

public enum IdentityAuditOutboxState
{
    Pending,
    Delivered,
    Poisoned,
}
