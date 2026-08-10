using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class WorkspaceContextTransition : AggregateRoot<Guid>
{
    public const int CorrelationDigestLength = 64;

    private WorkspaceContextTransition(
        Guid userId,
        Guid sourceWorkspaceId,
        Guid targetWorkspaceId,
        string sourceCorrelationDigest,
        string targetCorrelationDigest,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime retainUntil) : base(Guid.NewGuid())
    {
        if (userId == Guid.Empty || sourceWorkspaceId == Guid.Empty || targetWorkspaceId == Guid.Empty || sourceWorkspaceId == targetWorkspaceId)
            throw new ArgumentException("A transition requires distinct source and target workspaces for a user.");
        if (expiresAt <= createdAt || retainUntil < expiresAt)
            throw new ArgumentException("Transition expiry must be future and retention must outlive expiry.");

        UserId = userId;
        SourceWorkspaceId = sourceWorkspaceId;
        TargetWorkspaceId = targetWorkspaceId;
        TerminalAuditEventId = Guid.NewGuid();
        SourceCorrelationDigest = ValidateCorrelationDigest(sourceCorrelationDigest, nameof(sourceCorrelationDigest));
        TargetCorrelationDigest = ValidateCorrelationDigest(targetCorrelationDigest, nameof(targetCorrelationDigest));
        if (StringComparer.Ordinal.Equals(SourceCorrelationDigest, TargetCorrelationDigest))
            throw new ArgumentException("Source and target correlation digests must be distinct.");

        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RetainUntil = retainUntil;
        Status = WorkspaceContextTransitionStatus.Pending;
        Revision = 1;
    }

    public Guid UserId { get; private set; }
    public Guid SourceWorkspaceId { get; private set; }
    public Guid TargetWorkspaceId { get; private set; }
    public Guid TerminalAuditEventId { get; private set; }
    public string SourceCorrelationDigest { get; private set; }
    public string TargetCorrelationDigest { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime RetainUntil { get; private set; }
    public DateTime? TerminalAt { get; private set; }
    public DateTime? AuditProjectionConfirmedAt { get; private set; }
    public DateTime? RedisCleanupCompletedAt { get; private set; }
    public WorkspaceContextTransitionStatus Status { get; private set; }
    public int Revision { get; private set; }

    public static WorkspaceContextTransition Begin(
        Guid userId,
        Guid sourceWorkspaceId,
        Guid targetWorkspaceId,
        string sourceCorrelationDigest,
        string targetCorrelationDigest,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime retainUntil) => new(userId, sourceWorkspaceId, targetWorkspaceId, sourceCorrelationDigest, targetCorrelationDigest, createdAt, expiresAt, retainUntil);

    public void Complete(int expectedRevision, DateTime now) => Transition(WorkspaceContextTransitionStatus.Completed, expectedRevision, now);
    public void Compensate(int expectedRevision, DateTime now) => Transition(WorkspaceContextTransitionStatus.Compensated, expectedRevision, now);
    public void Fail(int expectedRevision, DateTime now) => Transition(WorkspaceContextTransitionStatus.Failed, expectedRevision, now);

    public void MarkAuditProjectionConfirmed(int expectedRevision, DateTime now)
    {
        MarkTerminalCompletion(expectedRevision, now, auditProjection: true);
    }

    public void MarkRedisCleanupCompleted(int expectedRevision, DateTime now)
    {
        MarkTerminalCompletion(expectedRevision, now, auditProjection: false);
    }

    public bool CanPurge(DateTime now) =>
        Status != WorkspaceContextTransitionStatus.Pending
        && now >= RetainUntil
        && AuditProjectionConfirmedAt.HasValue
        && RedisCleanupCompletedAt.HasValue;

    private void Transition(WorkspaceContextTransitionStatus state, int expectedRevision, DateTime now)
    {
        EnsureRevision(expectedRevision);
        if (Status != WorkspaceContextTransitionStatus.Pending)
            throw new InvalidOperationException("Only pending transitions can become terminal.");
        if (now > ExpiresAt && state == WorkspaceContextTransitionStatus.Completed)
            throw new InvalidOperationException("An expired transition cannot complete.");

        Status = state;
        TerminalAt = now;
        Revision++;
    }

    private void MarkTerminalCompletion(int expectedRevision, DateTime now, bool auditProjection)
    {
        if (Status == WorkspaceContextTransitionStatus.Pending)
            throw new InvalidOperationException("Only terminal transitions can record cleanup completion.");

        if (auditProjection)
        {
            if (AuditProjectionConfirmedAt.HasValue) return;
            EnsureRevision(expectedRevision);
            AuditProjectionConfirmedAt = now;
        }
        else
        {
            if (RedisCleanupCompletedAt.HasValue) return;
            EnsureRevision(expectedRevision);
            RedisCleanupCompletedAt = now;
        }
        Revision++;
    }

    private void EnsureRevision(int expectedRevision)
    {
        if (Revision != expectedRevision)
            throw new InvalidOperationException("Workspace context transition revision is stale.");
    }

    private static string ValidateCorrelationDigest(string value, string parameterName)
    {
        if (value is null
            || value.Length != CorrelationDigestLength
            || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("Transition correlation digest must be lowercase SHA-256 hex.", parameterName);
        }

        return value;
    }
}
