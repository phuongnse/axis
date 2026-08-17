using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinition : AggregateRoot<BusinessObjectDefinitionId>
{
    private readonly List<BusinessObjectFieldDefinition> _fields = [];
    private readonly List<BusinessObjectDefinitionVersion> _versions = [];

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; }
    public BusinessObjectDefinitionKey Key { get; private set; }
    public BusinessObjectDefinitionStatus Status { get; private set; }
    public int Revision { get; private set; }
    public int? LatestPublishedVersionNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    private ActorKind? CreatedByKind { get; set; }
    private Guid? CreatedBySubjectId { get; set; }
    private string? CreatedByDisplayName { get; set; }
    private ActorKind? UpdatedByKind { get; set; }
    private Guid? UpdatedBySubjectId { get; set; }
    private string? UpdatedByDisplayName { get; set; }
    public ActorSnapshot? CreatedBy => Snapshot(CreatedByKind, CreatedBySubjectId, CreatedByDisplayName);
    public ActorSnapshot? UpdatedBy => Snapshot(UpdatedByKind, UpdatedBySubjectId, UpdatedByDisplayName);
    public Guid? InstalledSolutionVersionId { get; private set; }
    public string? InstalledComponentKey { get; private set; }
    public string? InstalledComponentHash { get; private set; }
    public Guid? InstalledOperationId { get; private set; }
    public Guid? InstalledStepId { get; private set; }
    public long? InstalledLeaseEpoch { get; private set; }
    public bool IsInstalled => InstalledSolutionVersionId.HasValue;
    public IReadOnlyList<BusinessObjectFieldDefinition> Fields => _fields.AsReadOnly();
    public IReadOnlyList<BusinessObjectDefinitionVersion> Versions => _versions.AsReadOnly();

    private BusinessObjectDefinition()
        : base(default)
    {
        Name = string.Empty;
        Key = null!;
    }

    private BusinessObjectDefinition(
        BusinessObjectDefinitionId id,
        Guid workspaceId,
        string name,
        BusinessObjectDefinitionKey key,
        ActorSnapshot createdBy,
        DateTime createdAt)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        Key = key;
        Status = BusinessObjectDefinitionStatus.Unpublished;
        Revision = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        StampCreated(createdBy);
    }

    public static Result<BusinessObjectDefinition> CreateUnpublished(
        Guid workspaceId,
        string name,
        BusinessObjectDefinitionKey key,
        ActorSnapshot createdBy,
        DateTime createdAt)
    {
        if (workspaceId == Guid.Empty)
            return Result.Failure<BusinessObjectDefinition>("Workspace scope is required.");
        if (!createdBy.IsValid)
            return Result.Failure<BusinessObjectDefinition>("Creating actor is required.");

        Result normalizedName = ValidateName(name);
        if (normalizedName.IsFailure)
            return Result.Failure<BusinessObjectDefinition>(
                normalizedName.ErrorCode ?? ErrorCodes.InvalidInput,
                normalizedName.Error);

        return new BusinessObjectDefinition(
            BusinessObjectDefinitionId.New(),
            workspaceId,
            name.Trim(),
            key,
            createdBy,
            createdAt);
    }

    public Result RecordModification(ActorSnapshot actor)
    {
        if (!actor.IsValid)
            return Result.Failure(ErrorCodes.InvalidInput, "Modifying actor is required.");
        UpdatedByKind = actor.Kind;
        UpdatedBySubjectId = actor.SubjectId;
        UpdatedByDisplayName = actor.DisplayName;
        return Result.Success();
    }

    public Result SaveUnpublished(
        string name,
        IReadOnlyList<BusinessObjectFieldDefinitionSpec> fields,
        int expectedRevision,
        DateTime updatedAt)
    {
        if (IsInstalled)
            return Result.Failure(ErrorCodes.Conflict, "Installed business object definitions are immutable.");

        if (Status != BusinessObjectDefinitionStatus.Unpublished)
            return Result.Failure(ErrorCodes.Conflict, "Published definitions cannot be edited by this use case.");

        if (expectedRevision != Revision)
            return Result.Failure(ErrorCodes.Conflict, "The object definition has changed.");

        Result normalizedName = ValidateName(name);
        if (normalizedName.IsFailure)
            return normalizedName;

        Result<IReadOnlyList<BusinessObjectFieldDefinition>> unpublishedFields = CreateUnpublishedFields(fields);
        if (unpublishedFields.IsFailure)
            return Result.Failure(ErrorCodes.InvalidInput, unpublishedFields.Error);

        Name = name.Trim();
        ReplaceUnpublishedFields(unpublishedFields.Value);
        Revision += 1;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

    public Result<BusinessObjectDefinitionVersion> Publish(
        int expectedRevision,
        SubjectReference publishedBySubject,
        DateTime publishedAt)
    {
        if (IsInstalled)
        {
            return Result.Failure<BusinessObjectDefinitionVersion>(
                ErrorCodes.Conflict,
                "Installed business object definitions are immutable.");
        }

        if (publishedBySubject.Id == Guid.Empty || !Enum.IsDefined(publishedBySubject.Kind))
            return Result.Failure<BusinessObjectDefinitionVersion>("Publishing subject is required.");

        if (Status != BusinessObjectDefinitionStatus.Unpublished)
            return Result.Failure<BusinessObjectDefinitionVersion>(
                ErrorCodes.Conflict,
                "The object definition is already published.");

        if (expectedRevision != Revision)
            return Result.Failure<BusinessObjectDefinitionVersion>(
                ErrorCodes.Conflict,
                "The object definition has changed.");

        if (_fields.Count == 0)
            return Result.Failure<BusinessObjectDefinitionVersion>(
                ErrorCodes.InvalidInput,
                "Unpublished object definitions must have at least one field before publication.");

        const int firstPublishedVersion = 1;
        BusinessObjectDefinitionVersion version = BusinessObjectDefinitionVersion.Create(
            this,
            firstPublishedVersion,
            publishedBySubject,
            publishedAt);
        _versions.Add(version);
        Status = BusinessObjectDefinitionStatus.Published;
        LatestPublishedVersionNumber = firstPublishedVersion;
        UpdatedAt = publishedAt;
        return version;
    }

    public Result AdvanceInstallationReceipt(
        Guid solutionVersionId,
        string componentKey,
        string componentHash,
        Guid operationId,
        Guid stepId,
        long leaseEpoch)
    {
        if (solutionVersionId == Guid.Empty || operationId == Guid.Empty || stepId == Guid.Empty ||
            string.IsNullOrWhiteSpace(componentKey) || componentKey != componentKey.Trim() ||
            componentKey.Length > 200 || componentHash is not { Length: 64 } ||
            componentHash.Any(character => !char.IsAsciiHexDigit(character)) ||
            !StringComparer.Ordinal.Equals(componentHash, componentHash.ToLowerInvariant()) ||
            leaseEpoch <= 0)
        {
            return Result.Failure(ErrorCodes.InvalidInput, "Installation receipt is invalid.");
        }

        if (!IsInstalled)
        {
            if (Status != BusinessObjectDefinitionStatus.Published)
                return Result.Failure(ErrorCodes.Conflict, "Only a published definition can be installed.");

            InstalledSolutionVersionId = solutionVersionId;
            InstalledComponentKey = componentKey;
            InstalledComponentHash = componentHash;
            InstalledOperationId = operationId;
            InstalledStepId = stepId;
            InstalledLeaseEpoch = leaseEpoch;
            return Result.Success();
        }

        if (InstalledSolutionVersionId != solutionVersionId ||
            !StringComparer.Ordinal.Equals(InstalledComponentKey, componentKey) ||
            !StringComparer.Ordinal.Equals(InstalledComponentHash, componentHash) ||
            InstalledOperationId != operationId || InstalledStepId != stepId)
        {
            return Result.Failure(ErrorCodes.Conflict, "Installed business object provenance is immutable.");
        }
        if (leaseEpoch < InstalledLeaseEpoch!.Value)
            return Result.Failure(ErrorCodes.Conflict, "Installation receipt lease is stale.");

        InstalledLeaseEpoch = leaseEpoch;
        return Result.Success();
    }

    private Result<IReadOnlyList<BusinessObjectFieldDefinition>> CreateUnpublishedFields(
        IReadOnlyList<BusinessObjectFieldDefinitionSpec> specs)
    {
        HashSet<string> seenKeys = new(StringComparer.Ordinal);
        Dictionary<BusinessObjectFieldDefinitionId, BusinessObjectFieldDefinition> existingFieldsById = _fields
            .ToDictionary(field => field.Id);
        Dictionary<string, BusinessObjectFieldDefinition> existingFieldsByKey = _fields
            .ToDictionary(field => field.Key.Value, StringComparer.Ordinal);
        HashSet<BusinessObjectFieldDefinitionId> seenIds = [];
        List<BusinessObjectFieldDefinition> fields = [];

        foreach (BusinessObjectFieldDefinitionSpec spec in specs.OrderBy(field => field.Order))
        {
            string key = spec.FieldKey?.Trim() ?? string.Empty;
            if (!seenKeys.Add(key))
                return Result.Failure<IReadOnlyList<BusinessObjectFieldDefinition>>("Field keys must be unique.");

            Result<BusinessObjectFieldDefinitionId> fieldIdentity = ResolveFieldIdentity(
                spec,
                key,
                existingFieldsById,
                existingFieldsByKey,
                seenIds);
            if (fieldIdentity.IsFailure)
                return Result.Failure<IReadOnlyList<BusinessObjectFieldDefinition>>(fieldIdentity.Error);

            Result<BusinessObjectFieldDefinition> field =
                BusinessObjectFieldDefinition.Create(fieldIdentity.Value, spec);
            if (field.IsFailure)
                return Result.Failure<IReadOnlyList<BusinessObjectFieldDefinition>>(field.Error);

            fields.Add(field.Value);
        }

        return fields;
    }

    private void ReplaceUnpublishedFields(IReadOnlyList<BusinessObjectFieldDefinition> plannedFields)
    {
        Dictionary<BusinessObjectFieldDefinitionId, BusinessObjectFieldDefinition> existingFieldsById = _fields
            .ToDictionary(field => field.Id);
        List<BusinessObjectFieldDefinition> nextFields = [];

        foreach (BusinessObjectFieldDefinition plannedField in plannedFields.OrderBy(field => field.Order))
        {
            if (existingFieldsById.TryGetValue(plannedField.Id, out BusinessObjectFieldDefinition? existingField))
            {
                existingField.Apply(plannedField);
                nextFields.Add(existingField);
                continue;
            }

            nextFields.Add(plannedField);
        }

        _fields.Clear();
        _fields.AddRange(nextFields);
    }

    private void StampCreated(ActorSnapshot actor)
    {
        CreatedByKind = actor.Kind;
        CreatedBySubjectId = actor.SubjectId;
        CreatedByDisplayName = actor.DisplayName;
        UpdatedByKind = actor.Kind;
        UpdatedBySubjectId = actor.SubjectId;
        UpdatedByDisplayName = actor.DisplayName;
    }

    private static ActorSnapshot? Snapshot(
        ActorKind? kind,
        Guid? subjectId,
        string? displayName) =>
        kind is ActorKind actorKind && !string.IsNullOrWhiteSpace(displayName)
            ? ActorSnapshot.Create(actorKind, subjectId, displayName)
            : null;

    private static Result<BusinessObjectFieldDefinitionId> ResolveFieldIdentity(
        BusinessObjectFieldDefinitionSpec spec,
        string key,
        IReadOnlyDictionary<BusinessObjectFieldDefinitionId, BusinessObjectFieldDefinition> existingFieldsById,
        IReadOnlyDictionary<string, BusinessObjectFieldDefinition> existingFieldsByKey,
        ISet<BusinessObjectFieldDefinitionId> seenIds)
    {
        if (spec.Id is not BusinessObjectFieldDefinitionId fieldId)
        {
            return existingFieldsByKey.ContainsKey(key)
                ? Result.Failure<BusinessObjectFieldDefinitionId>(
                    "Existing field identity is required when saving an unpublished definition.")
                : BusinessObjectFieldDefinitionId.New();
        }

        if (!seenIds.Add(fieldId))
            return Result.Failure<BusinessObjectFieldDefinitionId>("Field identities must be unique.");

        if (!existingFieldsById.TryGetValue(fieldId, out BusinessObjectFieldDefinition? existingField))
            return Result.Failure<BusinessObjectFieldDefinitionId>(
                "Field identity does not belong to this business object definition.");

        if (!StringComparer.Ordinal.Equals(existingField.Key.Value, key))
            return Result.Failure<BusinessObjectFieldDefinitionId>("Persisted field keys cannot be changed.");

        Result childIdentity = existingField.ValidateChildIdentities(spec);
        return childIdentity.IsFailure
            ? Result.Failure<BusinessObjectFieldDefinitionId>(childIdentity.Error)
            : fieldId;
    }

    private static Result ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 256
            ? Result.Failure(ErrorCodes.InvalidInput, "Object definition name is required.")
            : Result.Success();
}
