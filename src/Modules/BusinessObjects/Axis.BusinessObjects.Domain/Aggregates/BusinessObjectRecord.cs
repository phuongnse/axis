using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectRecord : AggregateRoot<BusinessObjectRecordId>
{
    private Dictionary<string, IReadOnlyList<string>> _values = new(StringComparer.Ordinal);
    private List<BusinessObjectRecordRuleEvaluation> _ruleEvaluations = [];

    private BusinessObjectRecord()
        : base(default)
    {
        ObjectKey = BusinessObjectDefinitionKey.Create("record").Value;
        IdempotencyKey = string.Empty;
        PayloadHash = string.Empty;
    }

    private BusinessObjectRecord(
        BusinessObjectRecordId id,
        Guid workspaceId,
        BusinessObjectDefinitionVersionId definitionVersionId,
        int definitionVersionNumber,
        BusinessObjectDefinitionKey objectKey,
        string idempotencyKey,
        string payloadHash,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        SubjectReference createdBySubject,
        DateTime createdAt)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DefinitionVersionId = definitionVersionId;
        DefinitionVersionNumber = definitionVersionNumber;
        ObjectKey = objectKey;
        IdempotencyKey = idempotencyKey;
        PayloadHash = payloadHash;
        _values = CloneValues(values);
        Status = BusinessObjectRecordStatus.Draft;
        Revision = 1;
        Owner = createdBySubject;
        CreatedBySubject = createdBySubject;
        UpdatedBySubject = createdBySubject;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid WorkspaceId { get; private set; }
    public BusinessObjectDefinitionVersionId DefinitionVersionId { get; private set; }
    public int DefinitionVersionNumber { get; private set; }
    public BusinessObjectDefinitionKey ObjectKey { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayloadHash { get; private set; }
    public BusinessObjectRecordStatus Status { get; private set; }
    public int Revision { get; private set; }
    public SubjectReference Owner { get; private set; }
    public SubjectReference CreatedBySubject { get; private set; }
    public SubjectReference UpdatedBySubject { get; private set; }
    public SubjectReference? SubmittedBySubject { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Values =>
        _values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray(), StringComparer.Ordinal);
    public IReadOnlyList<BusinessObjectRecordRuleEvaluation> RuleEvaluations =>
        _ruleEvaluations.AsReadOnly();

    public static Result<BusinessObjectRecord> CreateDraft(
        Guid workspaceId,
        BusinessObjectDefinitionVersionId definitionVersionId,
        int definitionVersionNumber,
        BusinessObjectDefinitionKey objectKey,
        string idempotencyKey,
        string payloadHash,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        SubjectReference createdBySubject,
        DateTime createdAt)
    {
        if (workspaceId == Guid.Empty || definitionVersionId.Value == Guid.Empty || definitionVersionNumber <= 0 || createdBySubject.Id == Guid.Empty)
            return Result.Failure<BusinessObjectRecord>(ErrorCodes.InvalidInput, "Workspace, definition version, and user are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 120)
            return Result.Failure<BusinessObjectRecord>(ErrorCodes.InvalidInput, "An idempotency key is required and cannot exceed 120 characters.");
        if (string.IsNullOrWhiteSpace(payloadHash))
            return Result.Failure<BusinessObjectRecord>(ErrorCodes.InvalidInput, "A payload hash is required.");

        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> canonicalValues = CanonicalizeValues(values);
        if (canonicalValues.IsFailure)
            return Result.Failure<BusinessObjectRecord>(ErrorCodes.InvalidInput, canonicalValues.Error);

        return new BusinessObjectRecord(
            BusinessObjectRecordId.New(),
            workspaceId,
            definitionVersionId,
            definitionVersionNumber,
            objectKey,
            idempotencyKey.Trim(),
            payloadHash,
            canonicalValues.Value,
            createdBySubject,
            createdAt);
    }

    public Result SaveDraft(
        int expectedRevision,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        SubjectReference updatedBySubject,
        DateTime updatedAt)
    {
        if (Status != BusinessObjectRecordStatus.Draft)
            return Result.Failure(ErrorCodes.Conflict, "Submitted records cannot be edited.");
        if (expectedRevision != Revision)
            return Result.Failure(ErrorCodes.Conflict, "The record has changed.");
        if (updatedBySubject.Id == Guid.Empty)
            return Result.Failure(ErrorCodes.InvalidInput, "Updating user is required.");
        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> canonicalValues = CanonicalizeValues(values);
        if (canonicalValues.IsFailure)
            return Result.Failure(ErrorCodes.InvalidInput, canonicalValues.Error);

        _values = CloneValues(canonicalValues.Value);
        Revision += 1;
        UpdatedBySubject = updatedBySubject;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

    public Result Submit(
        int expectedRevision,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        IReadOnlyList<BusinessObjectRecordRuleEvaluation> evaluations,
        SubjectReference submittedBySubject,
        DateTime submittedAt)
    {
        if (Status != BusinessObjectRecordStatus.Draft)
            return Result.Failure(ErrorCodes.Conflict, "The record has already been submitted.");
        if (expectedRevision != Revision)
            return Result.Failure(ErrorCodes.Conflict, "The record has changed.");
        if (submittedBySubject.Id == Guid.Empty)
            return Result.Failure(ErrorCodes.InvalidInput, "Submitting user is required.");
        if (values is null)
            return Result.Failure(ErrorCodes.InvalidInput, "Record values are required.");
        if (evaluations is null)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule evaluation evidence is required.");

        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> canonicalValues = CanonicalizeValues(values);
        if (canonicalValues.IsFailure)
            return Result.Failure(ErrorCodes.InvalidInput, canonicalValues.Error);

        _values = CloneValues(canonicalValues.Value);
        _ruleEvaluations = evaluations.ToList();
        Status = BusinessObjectRecordStatus.Submitted;
        Revision += 1;
        UpdatedBySubject = submittedBySubject;
        SubmittedBySubject = submittedBySubject;
        UpdatedAt = submittedAt;
        SubmittedAt = submittedAt;
        return Result.Success();
    }

    private static Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> CanonicalizeValues(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        if (values is null)
            return Result.Failure<IReadOnlyDictionary<string, IReadOnlyList<string>>>("Record values are required.");

        Dictionary<string, IReadOnlyList<string>> canonical = new(StringComparer.Ordinal);
        foreach ((string key, IReadOnlyList<string> fieldValues) in values)
        {
            string normalizedKey = key?.Trim() ?? string.Empty;
            if (normalizedKey.Length == 0 || normalizedKey.Length > 63)
                return Result.Failure<IReadOnlyDictionary<string, IReadOnlyList<string>>>("Record field keys are invalid.");
            if (fieldValues is null || fieldValues.Count > 100 || fieldValues.Any(value => value is null))
                return Result.Failure<IReadOnlyDictionary<string, IReadOnlyList<string>>>("Record field values are invalid.");
            if (!canonical.TryAdd(
                    normalizedKey,
                    fieldValues.ToArray()))
            {
                return Result.Failure<IReadOnlyDictionary<string, IReadOnlyList<string>>>("Record field keys must be unique.");
            }
        }
        return canonical;
    }

    private static Dictionary<string, IReadOnlyList<string>> CloneValues(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        values.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
}
