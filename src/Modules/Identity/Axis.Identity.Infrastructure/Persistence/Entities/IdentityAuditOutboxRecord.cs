using System.Text.Json;
using Axis.Audit.Contracts;

namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class IdentityAuditOutboxRecord
{
    public Guid EventId { get; set; }
    public AuditActorKindV1 ActorKind { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Action { get; set; } = null!;
    public string TargetType { get; set; } = null!;
    public Guid TargetId { get; set; }
    public string Outcome { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string MetadataJson { get; set; } = "{}";
    public IdentityAuditOutboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Revision { get; set; }

    public AuditEventV1 ToAuditEvent() => new(
        EventId,
        ActorKind,
        ActorId,
        SubjectId,
        WorkspaceId,
        Action,
        TargetType,
        TargetId,
        Outcome,
        OccurredAt,
        CorrelationId,
        JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson));
}

internal enum IdentityAuditOutboxStatus
{
    Pending = 0,
    Delivered = 1,
    Poisoned = 2,
}
