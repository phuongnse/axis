using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class IdentityAuditOutbox(IdentityDbContext context) : IIdentityAuditOutbox
{
    public async Task EnqueueAsync(AuditEventV1 auditEvent, CancellationToken ct = default)
    {
        AuditEventValidationResult validation = AuditEventV1Validator.Validate(auditEvent);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"Audit event is invalid ({validation.RejectionCode}).",
                nameof(auditEvent));
        }

        await context.IdentityAuditOutboxRecords.AddAsync(new IdentityAuditOutboxRecord
        {
            EventId = auditEvent.EventId,
            ActorKind = auditEvent.ActorKind,
            ActorId = auditEvent.ActorId,
            SubjectId = auditEvent.SubjectId,
            WorkspaceId = auditEvent.WorkspaceId,
            Action = auditEvent.Action,
            TargetType = auditEvent.TargetType,
            TargetId = auditEvent.TargetId,
            Outcome = auditEvent.Outcome,
            OccurredAt = auditEvent.OccurredAt,
            CorrelationId = auditEvent.CorrelationId,
            MetadataJson = JsonSerializer.Serialize(
                auditEvent.Metadata ?? new Dictionary<string, string>()),
            Status = IdentityAuditOutboxStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
    }
}
