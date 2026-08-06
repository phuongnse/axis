using Axis.Audit.Application.Persistence;
using Axis.Audit.Contracts;
using Axis.Audit.Domain;

namespace Axis.Audit.Application;

public interface IAuditEventIngestionService
{
    Task<AuditIngestionResult> IngestAsync(AuditEventV1 auditEvent, CancellationToken cancellationToken = default);
    Task<AuditEventReadBackV1?> ReadBackAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public sealed class AuditEventIngestionService(
    IAuditRecordRepository repository,
    IAuditUnitOfWork unitOfWork) : IAuditEventIngestionService
{
    public async Task<AuditIngestionResult> IngestAsync(AuditEventV1 auditEvent, CancellationToken cancellationToken = default)
    {
        AuditEventValidationResult validation = AuditEventV1Validator.Validate(auditEvent);
        if (!validation.IsValid)
            return new(AuditIngestionDisposition.Rejected, null, validation.RejectionCode);

        if (!AuditRecord.TryCreate(
                auditEvent.EventId, (AuditActorKind)auditEvent.ActorKind, auditEvent.ActorId, auditEvent.SubjectId, auditEvent.WorkspaceId,
                auditEvent.Action, auditEvent.TargetType, auditEvent.TargetId, auditEvent.Outcome,
                auditEvent.OccurredAt, auditEvent.CorrelationId, auditEvent.Metadata, out AuditRecord? candidate,
                out string? rejectionCode))
            return new(AuditIngestionDisposition.Rejected, null, rejectionCode);

        AuditRecord? existing = await repository.GetByEventIdAsync(auditEvent.EventId, cancellationToken);
        if (existing is not null)
            return Existing(existing, auditEvent);

        await repository.AddAsync(candidate!, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new(AuditIngestionDisposition.Stored, ToReadBack(candidate!));
        }
        catch (AuditRecordAlreadyExistsException)
        {
            existing = await repository.GetByEventIdAsync(auditEvent.EventId, cancellationToken);
            return existing is null
                ? throw new InvalidOperationException("Audit record uniqueness conflict was not readable.")
                : Existing(existing, auditEvent);
        }
    }

    public async Task<AuditEventReadBackV1?> ReadBackAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
            return null;
        AuditRecord? record = await repository.GetByEventIdAsync(eventId, cancellationToken);
        return record is null ? null : ToReadBack(record);
    }

    private static AuditIngestionResult Existing(AuditRecord record, AuditEventV1 auditEvent) =>
        record.Matches((AuditActorKind)auditEvent.ActorKind, auditEvent.ActorId, auditEvent.SubjectId, auditEvent.WorkspaceId, auditEvent.Action,
            auditEvent.TargetType, auditEvent.TargetId, auditEvent.Outcome, auditEvent.OccurredAt,
            auditEvent.CorrelationId, auditEvent.Metadata)
            ? new(AuditIngestionDisposition.AlreadyStored, ToReadBack(record))
            : new(AuditIngestionDisposition.Conflict, ToReadBack(record), "audit.event_id_conflict");

    private static AuditEventReadBackV1 ToReadBack(AuditRecord record) => new(
        record.EventId, (AuditActorKindV1)record.ActorKind, record.ActorId, record.SubjectId, record.WorkspaceId, record.Action,
        record.TargetType, record.TargetId, record.Outcome, record.OccurredAt, record.CorrelationId,
        new Dictionary<string, string>(record.Metadata, StringComparer.Ordinal));
}
