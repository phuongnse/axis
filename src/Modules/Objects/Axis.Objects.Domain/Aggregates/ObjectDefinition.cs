using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinition : AggregateRoot<ObjectDefinitionId>
{
    private readonly List<ObjectFieldDefinition> _fields = [];
    private readonly List<ObjectDefinitionVersion> _versions = [];

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; }
    public ObjectDefinitionKey Key { get; private set; }
    public ObjectDefinitionStatus Status { get; private set; }
    public int DraftVersion { get; private set; }
    public int? LatestPublishedVersionNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public IReadOnlyList<ObjectFieldDefinition> Fields => _fields.AsReadOnly();
    public IReadOnlyList<ObjectDefinitionVersion> Versions => _versions.AsReadOnly();

    private ObjectDefinition(
        ObjectDefinitionId id,
        Guid workspaceId,
        string name,
        ObjectDefinitionKey key,
        DateTime createdAt)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        Key = key;
        Status = ObjectDefinitionStatus.Draft;
        DraftVersion = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static Result<ObjectDefinition> CreateDraft(
        Guid workspaceId,
        string name,
        ObjectDefinitionKey key,
        DateTime createdAt)
    {
        if (workspaceId == Guid.Empty)
            return Result.Failure<ObjectDefinition>("Workspace scope is required.");

        Result normalizedName = ValidateName(name);
        if (normalizedName.IsFailure)
            return Result.Failure<ObjectDefinition>(normalizedName.Error);

        return new ObjectDefinition(
            ObjectDefinitionId.New(),
            workspaceId,
            name.Trim(),
            key,
            createdAt);
    }

    public Result SaveDraft(
        string name,
        IReadOnlyList<ObjectFieldDefinitionSpec> fields,
        int expectedDraftVersion,
        DateTime updatedAt)
    {
        if (Status != ObjectDefinitionStatus.Draft)
            return Result.Failure(ErrorCodes.Conflict, "Published definitions cannot be edited by this use case.");

        if (expectedDraftVersion != DraftVersion)
            return Result.Failure(ErrorCodes.Conflict, "The object definition draft has changed.");

        Result normalizedName = ValidateName(name);
        if (normalizedName.IsFailure)
            return normalizedName;

        Result<IReadOnlyList<ObjectFieldDefinition>> draftFields = CreateDraftFields(fields);
        if (draftFields.IsFailure)
            return Result.Failure(ErrorCodes.InvalidInput, draftFields.Error);

        Name = name.Trim();
        ReplaceDraftFields(draftFields.Value);
        DraftVersion += 1;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

    public Result<ObjectDefinitionVersion> Publish(
        int expectedDraftVersion,
        Guid publishedByUserId,
        DateTime publishedAt)
    {
        if (publishedByUserId == Guid.Empty)
            return Result.Failure<ObjectDefinitionVersion>("Publishing user is required.");

        if (Status != ObjectDefinitionStatus.Draft)
            return Result.Failure<ObjectDefinitionVersion>(
                ErrorCodes.Conflict,
                "The object definition is already published.");

        if (expectedDraftVersion != DraftVersion)
            return Result.Failure<ObjectDefinitionVersion>(
                ErrorCodes.Conflict,
                "The object definition draft has changed.");

        if (_fields.Count == 0)
            return Result.Failure<ObjectDefinitionVersion>(
                ErrorCodes.InvalidInput,
                "Object definition drafts must have at least one field before publication.");

        const int firstPublishedVersion = 1;
        ObjectDefinitionVersion version = ObjectDefinitionVersion.Create(
            this,
            firstPublishedVersion,
            publishedByUserId,
            publishedAt);
        _versions.Add(version);
        Status = ObjectDefinitionStatus.Published;
        LatestPublishedVersionNumber = firstPublishedVersion;
        UpdatedAt = publishedAt;
        return version;
    }

    private Result<IReadOnlyList<ObjectFieldDefinition>> CreateDraftFields(
        IReadOnlyList<ObjectFieldDefinitionSpec> specs)
    {
        HashSet<string> seenKeys = new(StringComparer.Ordinal);
        Dictionary<string, ObjectFieldDefinition> existingFieldsByKey = _fields
            .ToDictionary(field => field.Key.Value, StringComparer.Ordinal);
        List<ObjectFieldDefinition> fields = [];

        foreach (ObjectFieldDefinitionSpec spec in specs.OrderBy(field => field.Order))
        {
            string key = spec.FieldKey?.Trim() ?? string.Empty;
            if (!seenKeys.Add(key))
                return Result.Failure<IReadOnlyList<ObjectFieldDefinition>>("Field keys must be unique.");

            ObjectFieldDefinitionId fieldId = existingFieldsByKey.TryGetValue(
                key,
                out ObjectFieldDefinition? existingField)
                ? existingField.Id
                : ObjectFieldDefinitionId.New();
            Result<ObjectFieldDefinition> field =
                ObjectFieldDefinition.Create(fieldId, spec);
            if (field.IsFailure)
                return Result.Failure<IReadOnlyList<ObjectFieldDefinition>>(field.Error);

            fields.Add(field.Value);
        }

        return fields;
    }

    private void ReplaceDraftFields(IReadOnlyList<ObjectFieldDefinition> plannedFields)
    {
        Dictionary<string, ObjectFieldDefinition> existingFieldsByKey = _fields
            .ToDictionary(field => field.Key.Value, StringComparer.Ordinal);
        List<ObjectFieldDefinition> nextFields = [];

        foreach (ObjectFieldDefinition plannedField in plannedFields.OrderBy(field => field.Order))
        {
            if (existingFieldsByKey.TryGetValue(plannedField.Key.Value, out ObjectFieldDefinition? existingField))
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

    private static Result ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Failure(ErrorCodes.InvalidInput, "Object definition name is required.")
            : Result.Success();
}
