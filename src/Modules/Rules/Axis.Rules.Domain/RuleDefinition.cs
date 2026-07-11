using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed class RuleDefinition : AggregateRoot<RuleDefinitionId>
{
    private readonly List<RuleParameterDefinition> _parameters = [];
    private readonly List<RuleDefinitionVersion> _versions = [];

    public Guid WorkspaceId { get; private set; }
    public RuleDefinitionKey Key { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public RuleScope Scope { get; private set; }
    public RuleContextKey ContextKey { get; private set; }
    public int ContextSchemaVersion { get; private set; }
    public RuleOutcomeKind OutcomeKind { get; private set; }
    public RuleLifecycleStatus Status { get; private set; }
    public int Revision { get; private set; }
    public int? LatestPublishedVersion { get; private set; }
    public RuleConditionNode? Condition { get; private set; }
    public RuleOutcome? Outcome { get; private set; }
    public IReadOnlyList<RuleParameterDefinition> Parameters => _parameters.AsReadOnly();
    public IReadOnlyList<RuleDefinitionVersion> Versions => _versions.AsReadOnly();
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
        ContextKey = default;
    }

    private RuleDefinition(
        RuleDefinitionId id,
        Guid workspaceId,
        RuleDefinitionKey key,
        string name,
        string description,
        RuleScope scope,
        RuleContextKey contextKey,
        int contextSchemaVersion,
        RuleOutcomeKind outcomeKind,
        Guid createdByUserId,
        DateTime createdAt)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Key = key;
        Name = name;
        Description = description;
        Scope = scope;
        ContextKey = contextKey;
        ContextSchemaVersion = contextSchemaVersion;
        OutcomeKind = outcomeKind;
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
        RuleScope scope,
        RuleContextKey contextKey,
        int contextSchemaVersion,
        RuleOutcomeKind outcomeKind,
        Guid createdByUserId,
        DateTime createdAt)
    {
        if (workspaceId == Guid.Empty)
            return Result.Failure<RuleDefinition>("Workspace scope is required.");

        if (createdByUserId == Guid.Empty)
            return Result.Failure<RuleDefinition>("Creating user is required.");

        Result<RuleDefinitionKey> canonicalKey = RuleDefinitionKey.Create(key.Value);
        if (canonicalKey.IsFailure)
            return Result.Failure<RuleDefinition>(canonicalKey.Error);

        Result<RuleContextKey> canonicalContextKey = RuleContextKey.Create(contextKey.Value);
        if (canonicalContextKey.IsFailure)
            return Result.Failure<RuleDefinition>(canonicalContextKey.Error);

        Result identity = ValidateIdentity(name, description, scope, contextSchemaVersion, outcomeKind);
        if (identity.IsFailure)
            return Result.Failure<RuleDefinition>(identity.Error);

        return new RuleDefinition(
            RuleDefinitionId.New(),
            workspaceId,
            canonicalKey.Value,
            name.Trim(),
            description.Trim(),
            scope,
            canonicalContextKey.Value,
            contextSchemaVersion,
            outcomeKind,
            createdByUserId,
            createdAt);
    }

    public Result SaveDraft(
        int expectedRevision,
        string name,
        string description,
        RuleScope scope,
        RuleContextKey contextKey,
        int contextSchemaVersion,
        RuleOutcomeKind outcomeKind,
        IReadOnlyList<RuleParameterDefinition> parameters,
        RuleConditionNode condition,
        RuleOutcome outcome,
        Guid updatedByUserId,
        DateTime updatedAt)
    {
        if (Status != RuleLifecycleStatus.Draft)
            return Result.Failure(ErrorCodes.Conflict, "Only a draft rule can be edited.");

        Result concurrency = ValidateMutation(expectedRevision, updatedByUserId);
        if (concurrency.IsFailure)
            return concurrency;

        Result identity = ValidateIdentity(name, description, scope, contextSchemaVersion, outcomeKind);
        if (identity.IsFailure)
            return identity;

        Result<RuleContextKey> canonicalContextKey = RuleContextKey.Create(contextKey.Value);
        if (canonicalContextKey.IsFailure)
            return Result.Failure(ErrorCodes.InvalidInput, canonicalContextKey.Error);

        Result draft = ValidateDraft(parameters, condition, outcome, outcomeKind);
        if (draft.IsFailure)
            return draft;

        Name = name.Trim();
        Description = description.Trim();
        Scope = scope;
        ContextKey = canonicalContextKey.Value;
        ContextSchemaVersion = contextSchemaVersion;
        OutcomeKind = outcomeKind;
        _parameters.Clear();
        _parameters.AddRange(parameters);
        Condition = condition;
        Outcome = outcome;
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
        if (Status != RuleLifecycleStatus.Draft)
            return Result.Failure<RuleDefinitionVersion>(ErrorCodes.Conflict, "Only a draft rule can be published.");

        Result concurrency = ValidateMutation(expectedRevision, publishedByUserId);
        if (concurrency.IsFailure)
            return Result.Failure<RuleDefinitionVersion>(concurrency.ErrorCode ?? ErrorCodes.InvalidInput, concurrency.Error);

        if (Condition is null || Outcome is null)
            return Result.Failure<RuleDefinitionVersion>(ErrorCodes.InvalidInput, "Rule draft must be configured before publication.");

        int versionNumber = (LatestPublishedVersion ?? 0) + 1;
        RuleDefinitionVersion version = RuleDefinitionVersion.Create(
            this,
            versionNumber,
            publishedByUserId,
            publishedAt);
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
        if (Status != RuleLifecycleStatus.Published)
            return Result.Failure(ErrorCodes.Conflict, "Only a published rule can start a new draft.");

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

    private Result ValidateMutation(int expectedRevision, Guid userId)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ErrorCodes.InvalidInput, "Acting user is required.");

        return expectedRevision == Revision
            ? Result.Success()
            : Result.Failure(ErrorCodes.Conflict, "The rule definition has changed.");
    }

    private static Result ValidateIdentity(
        string name,
        string description,
        RuleScope scope,
        int contextSchemaVersion,
        RuleOutcomeKind outcomeKind)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule name is required and cannot exceed 200 characters.");

        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 1000)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule description is required and cannot exceed 1000 characters.");

        if (!Enum.IsDefined(scope))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule scope is not supported.");

        if (contextSchemaVersion <= 0)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule context schema version must be positive.");

        if (!Enum.IsDefined(outcomeKind))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule outcome kind is not supported.");

        return Result.Success();
    }

    private static Result ValidateDraft(
        IReadOnlyList<RuleParameterDefinition> parameters,
        RuleConditionNode condition,
        RuleOutcome outcome,
        RuleOutcomeKind outcomeKind)
    {
        if (parameters is null || parameters.Any(parameter => parameter is null))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule parameters are required.");

        if (parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count() != parameters.Count)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule parameter keys must be unique.");

        if (condition is null)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule condition is required.");

        if (outcome is null || outcome.Kind != outcomeKind)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule outcome does not match the selected outcome kind.");

        return Result.Success();
    }
}
