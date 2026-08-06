using System.Collections.ObjectModel;

namespace Axis.Audit.Domain;

public enum AuditActorKind
{
    Human = 0,
    ServiceIdentity = 1,
    System = 2,
    Anonymous = 3,
}

public sealed class AuditRecord
{
    private const int MaximumMetadataEntries = 16;
    private const int MaximumMetadataKeyLength = 64;
    private const int MaximumMetadataValueLength = 256;
    private const int MaximumMetadataLength = 2048;
    private Dictionary<string, string> _metadata = new(StringComparer.Ordinal);

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public AuditActorKind ActorKind { get; private set; }
    public Guid? ActorId { get; private set; }
    public Guid? SubjectId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid TargetId { get; private set; }
    public string Outcome { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata => new ReadOnlyDictionary<string, string>(_metadata);

    private AuditRecord()
    {
    }

    private AuditRecord(
        Guid eventId,
        AuditActorKind actorKind,
        Guid? actorId,
        Guid? subjectId,
        Guid workspaceId,
        string action,
        string targetType,
        Guid targetId,
        string outcome,
        DateTimeOffset occurredAt,
        string correlationId,
        IReadOnlyDictionary<string, string> metadata)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        ActorKind = actorKind;
        ActorId = actorId;
        SubjectId = subjectId;
        WorkspaceId = workspaceId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Outcome = outcome;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        _metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal);
    }

    public static bool TryCreate(
        Guid eventId,
        AuditActorKind actorKind,
        Guid? actorId,
        Guid? subjectId,
        Guid workspaceId,
        string? action,
        string? targetType,
        Guid targetId,
        string? outcome,
        DateTimeOffset occurredAt,
        string? correlationId,
        IReadOnlyDictionary<string, string>? metadata,
        out AuditRecord? record,
        out string? rejectionCode)
    {
        record = null;
        rejectionCode = Validate(eventId, actorKind, actorId, subjectId, workspaceId, action, targetType, targetId, outcome, occurredAt, correlationId, metadata);
        if (rejectionCode is not null)
            return false;

        record = new AuditRecord(
            eventId,
            actorKind,
            actorId,
            subjectId,
            workspaceId,
            action!.Trim(),
            targetType!.Trim(),
            targetId,
            outcome!.Trim(),
            NormalizeOccurredAt(occurredAt),
            correlationId!.Trim(),
            metadata ?? new Dictionary<string, string>());
        return true;
    }

    public bool Matches(
        AuditActorKind actorKind,
        Guid? actorId,
        Guid? subjectId,
        Guid workspaceId,
        string action,
        string targetType,
        Guid targetId,
        string outcome,
        DateTimeOffset occurredAt,
        string correlationId,
        IReadOnlyDictionary<string, string>? metadata) =>
        ActorKind == actorKind &&
        ActorId == actorId &&
        SubjectId == subjectId &&
        WorkspaceId == workspaceId &&
        Action == action.Trim() &&
        TargetType == targetType.Trim() &&
        TargetId == targetId &&
        Outcome == outcome.Trim() &&
        OccurredAt == NormalizeOccurredAt(occurredAt) &&
        CorrelationId == correlationId.Trim() &&
        _metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual((metadata ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal));

    private static string? Validate(
        Guid eventId,
        AuditActorKind actorKind,
        Guid? actorId,
        Guid? subjectId,
        Guid workspaceId,
        string? action,
        string? targetType,
        Guid targetId,
        string? outcome,
        DateTimeOffset occurredAt,
        string? correlationId,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (eventId == Guid.Empty || workspaceId == Guid.Empty || targetId == Guid.Empty)
            return "audit.identifier_invalid";
        if (!HasValidActor(actorKind, actorId))
            return "audit.actor_invalid";
        if (subjectId == Guid.Empty)
            return "audit.subject_invalid";
        if (!IsCategory(action, 64) || !IsCategory(targetType, 64) || !IsCategory(outcome, 64))
            return "audit.category_invalid";
        if (occurredAt == default || string.IsNullOrWhiteSpace(correlationId) || correlationId.Trim().Length > 120)
            return "audit.correlation_invalid";
        return ValidateMetadata(metadata);
    }

    private static bool HasValidActor(AuditActorKind actorKind, Guid? actorId) => actorKind switch
    {
        AuditActorKind.Human or AuditActorKind.ServiceIdentity => actorId is Guid id && id != Guid.Empty,
        AuditActorKind.System or AuditActorKind.Anonymous => actorId is null,
        _ => false,
    };

    private static string? ValidateMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
            return null;
        if (metadata.Count > MaximumMetadataEntries)
            return "audit.metadata_invalid";

        int length = 0;
        foreach ((string key, string value) in metadata)
        {
            if (!IsCategory(key, MaximumMetadataKeyLength) || IsSensitiveKey(key) || value is null || value.Length > MaximumMetadataValueLength)
                return "audit.metadata_invalid";
            length += key.Length + value.Length;
            if (length > MaximumMetadataLength)
                return "audit.metadata_invalid";
        }

        return null;
    }

    private static bool IsCategory(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Trim().Length <= maximumLength &&
        value.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSensitiveKey(string key) =>
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("handoff", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("envelope", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset NormalizeOccurredAt(DateTimeOffset occurredAt) =>
        occurredAt.AddTicks(-(occurredAt.Ticks % 10));
}
