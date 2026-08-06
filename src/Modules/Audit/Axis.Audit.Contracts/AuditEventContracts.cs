namespace Axis.Audit.Contracts;

public enum AuditActorKindV1
{
    Human = 0,
    ServiceIdentity = 1,
    System = 2,
    Anonymous = 3,
}

public sealed record AuditEventValidationResult(bool IsValid, string? RejectionCode)
{
    public static AuditEventValidationResult Valid { get; } = new(true, null);
}

public static class AuditEventV1Validator
{
    public const int MaximumCategoryLength = 64;
    public const int MaximumCorrelationIdLength = 120;
    public const int MaximumMetadataEntries = 16;
    public const int MaximumMetadataKeyLength = 64;
    public const int MaximumMetadataValueLength = 256;
    public const int MaximumMetadataLength = 2048;

    public static AuditEventValidationResult Validate(AuditEventV1? auditEvent)
    {
        if (auditEvent is null)
            return Invalid("audit.envelope_invalid");
        if (auditEvent.EventId == Guid.Empty || auditEvent.WorkspaceId == Guid.Empty || auditEvent.TargetId == Guid.Empty)
            return Invalid("audit.identifier_invalid");
        if (!HasValidActor(auditEvent.ActorKind, auditEvent.ActorId))
            return Invalid("audit.actor_invalid");
        if (!HasValidScope(auditEvent.ActorKind, auditEvent.WorkspaceId))
            return Invalid("audit.scope_invalid");
        if (auditEvent.SubjectId == Guid.Empty)
            return Invalid("audit.subject_invalid");
        if (!IsCategory(auditEvent.Action, MaximumCategoryLength) ||
            !IsCategory(auditEvent.TargetType, MaximumCategoryLength) ||
            !IsCategory(auditEvent.Outcome, MaximumCategoryLength))
            return Invalid("audit.category_invalid");
        if (auditEvent.OccurredAt == default || string.IsNullOrWhiteSpace(auditEvent.CorrelationId) ||
            auditEvent.CorrelationId.Trim().Length > MaximumCorrelationIdLength)
            return Invalid("audit.correlation_invalid");
        return ValidateMetadata(auditEvent.Metadata);
    }

    private static AuditEventValidationResult ValidateMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
            return AuditEventValidationResult.Valid;
        if (metadata.Count > MaximumMetadataEntries)
            return Invalid("audit.metadata_invalid");

        int length = 0;
        foreach ((string key, string value) in metadata)
        {
            if (!IsCategory(key, MaximumMetadataKeyLength) || IsSensitiveKey(key) || value is null ||
                value.Length > MaximumMetadataValueLength)
                return Invalid("audit.metadata_invalid");
            length += key.Length + value.Length;
            if (length > MaximumMetadataLength)
                return Invalid("audit.metadata_invalid");
        }

        return AuditEventValidationResult.Valid;
    }

    private static bool HasValidActor(AuditActorKindV1 actorKind, Guid? actorId) => actorKind switch
    {
        AuditActorKindV1.Human or AuditActorKindV1.ServiceIdentity => actorId is Guid id && id != Guid.Empty,
        AuditActorKindV1.System or AuditActorKindV1.Anonymous => actorId is null,
        _ => false,
    };

    private static bool HasValidScope(AuditActorKindV1 actorKind, Guid? workspaceId) => actorKind switch
    {
        AuditActorKindV1.Human or AuditActorKindV1.ServiceIdentity => workspaceId is Guid id && id != Guid.Empty,
        AuditActorKindV1.System or AuditActorKindV1.Anonymous => workspaceId != Guid.Empty,
        _ => false,
    };

    private static bool IsCategory(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSensitiveKey(string key) =>
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("handoff", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("envelope", StringComparison.OrdinalIgnoreCase);

    private static AuditEventValidationResult Invalid(string rejectionCode) => new(false, rejectionCode);
}

public sealed record AuditEventV1(
    Guid EventId,
    AuditActorKindV1 ActorKind,
    Guid? ActorId,
    Guid? SubjectId,
    Guid? WorkspaceId,
    string Action,
    string TargetType,
    Guid TargetId,
    string Outcome,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AuditEventReadBackV1(
    Guid EventId,
    AuditActorKindV1 ActorKind,
    Guid? ActorId,
    Guid? SubjectId,
    Guid? WorkspaceId,
    string Action,
    string TargetType,
    Guid TargetId,
    string Outcome,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Metadata);

public static class AuditEventV1ReadBack
{
    public static bool Matches(AuditEventV1 expected, AuditEventReadBackV1 actual) =>
        expected.EventId == actual.EventId
        && expected.ActorKind == actual.ActorKind
        && expected.ActorId == actual.ActorId
        && expected.SubjectId == actual.SubjectId
        && expected.WorkspaceId == actual.WorkspaceId
        && StringComparer.Ordinal.Equals(expected.Action.Trim(), actual.Action)
        && StringComparer.Ordinal.Equals(expected.TargetType.Trim(), actual.TargetType)
        && expected.TargetId == actual.TargetId
        && StringComparer.Ordinal.Equals(expected.Outcome.Trim(), actual.Outcome)
        && NormalizeOccurredAt(expected.OccurredAt) == actual.OccurredAt
        && StringComparer.Ordinal.Equals(expected.CorrelationId.Trim(), actual.CorrelationId)
        && (expected.Metadata ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(actual.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));

    private static DateTimeOffset NormalizeOccurredAt(DateTimeOffset occurredAt) =>
        occurredAt.AddTicks(-(occurredAt.Ticks % 10));
}

public enum AuditIngestionDisposition
{
    Stored = 0,
    AlreadyStored = 1,
    Conflict = 2,
    Rejected = 3,
}

public sealed record AuditIngestionResult(
    AuditIngestionDisposition Disposition,
    AuditEventReadBackV1? Event,
    string? RejectionCode = null);

public interface IAuditEventSink
{
    Task<AuditIngestionResult> IngestAsync(
        AuditEventV1 auditEvent,
        CancellationToken cancellationToken = default);

    Task<AuditEventReadBackV1?> ReadBackAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}
