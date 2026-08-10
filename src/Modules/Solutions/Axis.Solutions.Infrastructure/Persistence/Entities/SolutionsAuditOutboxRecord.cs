using Axis.Audit.Contracts;
using Axis.Solutions.Domain;

namespace Axis.Solutions.Infrastructure.Persistence.Entities;

internal sealed class SolutionsAuditOutboxRecord
{
    public Guid EventId { get; set; }
    public AuditActorKindV1 ActorKind { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? SubjectId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public SolutionSubjectKind? OriginatingSubjectKind { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid? WorkspaceId { get; set; }
    public Guid? SolutionVersionId { get; set; }
    public Guid? InstallationId { get; set; }
    public Guid? OperationId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? ProblemCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public int Revision { get; set; }
}
