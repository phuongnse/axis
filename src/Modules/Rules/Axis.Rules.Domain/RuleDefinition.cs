using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed class RuleDefinition : AggregateRoot<RuleDefinitionId>
{
    private readonly List<RuleInputDefinition> _inputs = [];
    private readonly List<RuleDefinitionVersion> _versions = [];

    public Guid WorkspaceId { get; private set; }
    public RuleDefinitionKey Key { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public RuleOrigin Origin { get; private set; }
    public int ExpressionLanguageVersion { get; private set; }
    public RuleLifecycleStatus Status { get; private set; }
    public int Revision { get; private set; }
    public int? LatestPublishedVersion { get; private set; }
    public RuleConditionNode? Condition { get; private set; }
    public RuleOutputContract Output { get; private set; }
    public IReadOnlyList<RuleInputDefinition> Inputs => _inputs.AsReadOnly();
    public IReadOnlyList<RuleDefinitionVersion> Versions => _versions.AsReadOnly();
    public RuleReferenceDocumentation? Documentation { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public DateTime? ArchivedAt { get; private set; }

    private RuleDefinition()
        : base(default)
    {
        Key = default;
        Name = string.Empty;
        Description = string.Empty;
        Output = RuleOutputContract.BooleanMatch;
    }

    private RuleDefinition(
        RuleDefinitionId id,
        Guid workspaceId,
        RuleDefinitionKey key,
        string name,
        string description,
        RuleOrigin origin,
        Guid createdByUserId,
        DateTime createdAt)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Key = key;
        Name = name;
        Description = description;
        Origin = origin;
        ExpressionLanguageVersion = RuleExpressionLanguage.Version;
        Output = RuleOutputContract.BooleanMatch;
        Status = RuleLifecycleStatus.Draft;
        Revision = 1;
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static Result<RuleDefinition> CreateDraft(
        Guid workspaceId,
        RuleDefinitionKey key,
        string name,
        string description,
        Guid createdByUserId,
        DateTime createdAt)
    {
        if (workspaceId == Guid.Empty)
            return Result.Failure<RuleDefinition>("Workspace is required.");

        if (createdByUserId == Guid.Empty)
            return Result.Failure<RuleDefinition>("Creating user is required.");

        Result<RuleDefinitionKey> canonicalKey = RuleDefinitionKey.Create(key.Value);
        if (canonicalKey.IsFailure)
            return Result.Failure<RuleDefinition>(canonicalKey.Error);

        Result identity = ValidateIdentity(name, description);
        if (identity.IsFailure)
            return Result.Failure<RuleDefinition>(identity.Error);

        return new RuleDefinition(
            RuleDefinitionId.New(),
            workspaceId,
            canonicalKey.Value,
            name.Trim(),
            description.Trim(),
            RuleOrigin.Workspace,
            createdByUserId,
            createdAt);
    }

    public static Result<RuleDefinition> CreateSystem(
        RuleDefinitionKey key,
        int version,
        string name,
        string description,
        RuleReferenceDocumentation documentation,
        IReadOnlyList<RuleInputDefinition> inputs,
        RuleConditionNode condition,
        RuleOutputContract output)
    {
        Result identity = ValidateIdentity(name, description);
        if (identity.IsFailure)
            return Result.Failure<RuleDefinition>(identity.Error);

        if (version <= 0)
            return Result.Failure<RuleDefinition>("System rule version must be positive.");

        if (documentation is null || !documentation.IsComplete("en", "vi"))
            return Result.Failure<RuleDefinition>("System rule documentation is incomplete.");

        Result semantic = RuleDefinitionValidator.Validate(inputs, condition, output);
        if (semantic.IsFailure)
            return Result.Failure<RuleDefinition>(semantic.Error);

        RuleDefinition definition = new(
            RuleDefinitionId.New(),
            Guid.Empty,
            key,
            name.Trim(),
            description.Trim(),
            RuleOrigin.System,
            Guid.Empty,
            default)
        {
            Documentation = documentation,
            Status = RuleLifecycleStatus.Published,
            Revision = 0,
            LatestPublishedVersion = version,
            Condition = condition,
            Output = output,
        };
        definition._inputs.AddRange(inputs);
        definition._versions.Add(RuleDefinitionVersion.Create(definition, version, Guid.Empty, default));
        return definition;
    }

    public Result SaveDraft(
        int expectedRevision,
        string name,
        string description,
        IReadOnlyList<RuleInputDefinition> inputs,
        RuleConditionNode condition,
        Guid updatedByUserId,
        DateTime updatedAt)
    {
        if (Origin != RuleOrigin.Workspace)
            return Result.Failure(ErrorCodes.Conflict, "System rules are read-only.");

        if (Status != RuleLifecycleStatus.Draft)
            return Result.Failure(ErrorCodes.Conflict, "Only a draft rule can be edited.");

        Result concurrency = ValidateMutation(expectedRevision, updatedByUserId);
        if (concurrency.IsFailure)
            return concurrency;

        Result identity = ValidateIdentity(name, description);
        if (identity.IsFailure)
            return identity;

        Result semantic = RuleDefinitionValidator.Validate(inputs, condition, Output);
        if (semantic.IsFailure)
            return semantic;

        Name = name.Trim();
        Description = description.Trim();
        _inputs.Clear();
        _inputs.AddRange(inputs);
        Condition = condition;
        Revision += 1;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

    public Result<RuleDefinitionVersion> Publish(
        int expectedRevision,
        Guid publishedByUserId,
        DateTime publishedAt)
    {
        if (Origin != RuleOrigin.Workspace)
            return Result.Failure<RuleDefinitionVersion>(ErrorCodes.Conflict, "System rules are read-only.");

        if (Status != RuleLifecycleStatus.Draft)
            return Result.Failure<RuleDefinitionVersion>(ErrorCodes.Conflict, "Only a draft rule can be published.");

        Result concurrency = ValidateMutation(expectedRevision, publishedByUserId);
        if (concurrency.IsFailure)
            return Result.Failure<RuleDefinitionVersion>(concurrency.ErrorCode ?? ErrorCodes.InvalidInput, concurrency.Error);

        if (Condition is null)
            return Result.Failure<RuleDefinitionVersion>(ErrorCodes.InvalidInput, "Rule draft must be configured before publication.");

        int versionNumber = (LatestPublishedVersion ?? 0) + 1;
        RuleDefinitionVersion version = RuleDefinitionVersion.Create(this, versionNumber, publishedByUserId, publishedAt);
        _versions.Add(version);
        LatestPublishedVersion = versionNumber;
        Status = RuleLifecycleStatus.Published;
        Revision += 1;
        UpdatedByUserId = publishedByUserId;
        UpdatedAt = publishedAt;
        return version;
    }

    public Result StartNextDraft(int expectedRevision, Guid updatedByUserId, DateTime updatedAt)
    {
        if (Origin != RuleOrigin.Workspace || Status != RuleLifecycleStatus.Published)
            return Result.Failure(ErrorCodes.Conflict, "Only a published workspace rule can start a new draft.");

        Result concurrency = ValidateMutation(expectedRevision, updatedByUserId);
        if (concurrency.IsFailure)
            return concurrency;

        Status = RuleLifecycleStatus.Draft;
        Revision += 1;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

    public Result Archive(int expectedRevision, Guid archivedByUserId, DateTime archivedAt)
    {
        if (Origin != RuleOrigin.Workspace)
            return Result.Failure(ErrorCodes.Conflict, "System rules are read-only.");

        if (Status == RuleLifecycleStatus.Archived)
            return Result.Success();

        if (LatestPublishedVersion is null)
            return Result.Failure(ErrorCodes.InvalidInput, "A rule must be published before it can be archived.");

        Result concurrency = ValidateMutation(expectedRevision, archivedByUserId);
        if (concurrency.IsFailure)
            return concurrency;

        Status = RuleLifecycleStatus.Archived;
        Revision += 1;
        UpdatedByUserId = archivedByUserId;
        UpdatedAt = archivedAt;
        ArchivedByUserId = archivedByUserId;
        ArchivedAt = archivedAt;
        return Result.Success();
    }

    public RuleDefinitionVersion? FindVersion(int version) =>
        _versions.SingleOrDefault(candidate => candidate.Version == version);

    private Result ValidateMutation(int expectedRevision, Guid userId) =>
        userId == Guid.Empty
            ? Result.Failure(ErrorCodes.InvalidInput, "Acting user is required.")
            : expectedRevision == Revision
                ? Result.Success()
                : Result.Failure(ErrorCodes.Conflict, "The rule definition has changed.");

    private static Result ValidateIdentity(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule name is required and cannot exceed 200 characters.");

        return string.IsNullOrWhiteSpace(description) || description.Trim().Length > 1000
            ? Result.Failure(ErrorCodes.InvalidInput, "Rule description is required and cannot exceed 1000 characters.")
            : Result.Success();
    }
}
