using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial class RuleBinding : AggregateRoot<RuleBindingId>
{
    private Dictionary<string, RuleInputMapping> _inputMappings = new(StringComparer.Ordinal);
    private List<RuleBindingRevision> _revisionHistory = [];

    private RuleBinding()
        : base(default)
    {
        DefinitionKey = default;
        TargetType = string.Empty;
        TargetId = string.Empty;
        UseCaseOrTrigger = string.Empty;
    }

    private RuleBinding(
        RuleBindingId id,
        Guid workspaceId,
        RuleDefinitionKey definitionKey,
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        IReadOnlyDictionary<string, RuleInputMapping> inputMappings,
        int priority,
        bool enabled,
        RuleBindingFailureBehavior failureBehavior,
        RuleSubjectReference createdBySubject,
        DateTime createdAt)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DefinitionKey = definitionKey;
        DefinitionVersion = definitionVersion;
        TargetType = targetType;
        TargetId = targetId;
        UseCaseOrTrigger = useCaseOrTrigger;
        _inputMappings = Clone(inputMappings);
        Priority = priority;
        Enabled = enabled;
        FailureBehavior = failureBehavior;
        Revision = 1;
        CreatedBySubject = createdBySubject;
        UpdatedBySubject = createdBySubject;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        _revisionHistory.Add(CreateRevision(createdBySubject, createdAt));
    }

    public Guid WorkspaceId { get; private set; }
    public RuleDefinitionKey DefinitionKey { get; private set; }
    public int DefinitionVersion { get; private set; }
    public string TargetType { get; private set; }
    public string TargetId { get; private set; }
    public string UseCaseOrTrigger { get; private set; }
    public IReadOnlyDictionary<string, RuleInputMapping> InputMappings =>
        _inputMappings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public RuleBindingFailureBehavior FailureBehavior { get; private set; }
    public int Revision { get; private set; }
    public RuleSubjectReference CreatedBySubject { get; private set; }
    public RuleSubjectReference UpdatedBySubject { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? InstalledSolutionVersionId { get; private set; }
    public string? InstalledComponentKey { get; private set; }
    public string? InstalledComponentHash { get; private set; }
    public Guid? InstalledOperationId { get; private set; }
    public Guid? InstalledStepId { get; private set; }
    public long? InstalledLeaseEpoch { get; private set; }
    public bool IsInstalled => InstalledSolutionVersionId.HasValue;
    public IReadOnlyList<RuleBindingRevision> RevisionHistory => _revisionHistory.AsReadOnly();

    public static Result<RuleBinding> Create(
        Guid workspaceId,
        RuleDefinitionKey definitionKey,
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        IReadOnlyDictionary<string, RuleInputMapping> inputMappings,
        int priority,
        bool enabled,
        RuleBindingFailureBehavior failureBehavior,
        RuleSubjectReference createdBySubject,
        DateTime createdAt)
    {
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(definitionKey.Value);
        if (workspaceId == Guid.Empty || createdBySubject.Id == Guid.Empty || !Enum.IsDefined(createdBySubject.Kind))
            return Result.Failure<RuleBinding>(ErrorCodes.InvalidInput, "Workspace and acting subject are required.");
        if (key.IsFailure)
            return Result.Failure<RuleBinding>(ErrorCodes.InvalidInput, key.Error);
        Result common = ValidateCommon(definitionVersion, targetType, targetId, useCaseOrTrigger, inputMappings, priority, failureBehavior);
        return common.IsFailure
            ? Result.Failure<RuleBinding>(common.ErrorCode ?? ErrorCodes.InvalidInput, common.Error)
            : new RuleBinding(
                RuleBindingId.New(),
                workspaceId,
                key.Value,
                definitionVersion,
                targetType.Trim(),
                targetId.Trim(),
                useCaseOrTrigger.Trim(),
                inputMappings,
                priority,
                enabled,
                failureBehavior,
                createdBySubject,
                createdAt);
    }

    public static Result ValidateInstallationCandidate(
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        IReadOnlyDictionary<string, RuleInputMapping> inputMappings,
        int priority,
        RuleBindingFailureBehavior failureBehavior) =>
        ValidateCommon(
            definitionVersion,
            targetType,
            targetId,
            useCaseOrTrigger,
            inputMappings,
            priority,
            failureBehavior);

    public Result Update(
        int expectedRevision,
        RuleDefinitionKey definitionKey,
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        IReadOnlyDictionary<string, RuleInputMapping> inputMappings,
        int priority,
        bool enabled,
        RuleBindingFailureBehavior failureBehavior,
        RuleSubjectReference updatedBySubject,
        DateTime updatedAt)
    {
        if (IsInstalled)
            return Result.Failure(ErrorCodes.Conflict, "Installed rule bindings are immutable.");
        if (updatedBySubject.Id == Guid.Empty || !Enum.IsDefined(updatedBySubject.Kind))
            return Result.Failure(ErrorCodes.InvalidInput, "Acting subject is required.");
        if (expectedRevision != Revision)
            return Result.Failure(ErrorCodes.Conflict, "The rule binding has changed.");
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(definitionKey.Value);
        if (key.IsFailure)
            return Result.Failure(ErrorCodes.InvalidInput, key.Error);
        Result common = ValidateCommon(definitionVersion, targetType, targetId, useCaseOrTrigger, inputMappings, priority, failureBehavior);
        if (common.IsFailure)
            return common;

        DefinitionKey = key.Value;
        DefinitionVersion = definitionVersion;
        TargetType = targetType.Trim();
        TargetId = targetId.Trim();
        UseCaseOrTrigger = useCaseOrTrigger.Trim();
        _inputMappings = Clone(inputMappings);
        Priority = priority;
        Enabled = enabled;
        FailureBehavior = failureBehavior;
        Revision += 1;
        UpdatedBySubject = updatedBySubject;
        UpdatedAt = updatedAt;
        _revisionHistory.Add(CreateRevision(updatedBySubject, updatedAt));
        return Result.Success();
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
            componentKey.Length > 200 || componentHash.Length != 64 ||
            componentHash.Any(character => !char.IsAsciiHexDigit(character)) ||
            !StringComparer.Ordinal.Equals(componentHash, componentHash.ToLowerInvariant()) ||
            leaseEpoch <= 0)
        {
            return Result.Failure(ErrorCodes.InvalidInput, "Installation receipt is invalid.");
        }

        if (!IsInstalled)
        {
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
            return Result.Failure(ErrorCodes.Conflict, "Installed rule binding provenance is immutable.");
        }
        if (leaseEpoch < InstalledLeaseEpoch!.Value)
            return Result.Failure(ErrorCodes.Conflict, "Installation receipt lease is stale.");

        InstalledLeaseEpoch = leaseEpoch;
        return Result.Success();
    }

    public RuleBindingRevision? FindRevision(int? revision)
    {
        int requestedRevision = revision ?? Revision;
        if (requestedRevision <= 0 || requestedRevision > Revision)
            return null;

        RuleBindingRevision? historical = _revisionHistory
            .SingleOrDefault(candidate => candidate.Revision == requestedRevision);
        return historical ??
            (requestedRevision == Revision
                ? CreateRevision(UpdatedBySubject, UpdatedAt)
                : null);
    }

    private RuleBindingRevision CreateRevision(RuleSubjectReference updatedBySubject, DateTime updatedAt) =>
        new(
            Revision,
            DefinitionKey,
            DefinitionVersion,
            TargetType,
            TargetId,
            UseCaseOrTrigger,
            Clone(_inputMappings),
            Priority,
            Enabled,
            FailureBehavior,
            updatedBySubject,
            updatedAt);

    private static Result ValidateCommon(
        int definitionVersion,
        string targetType,
        string targetId,
        string useCaseOrTrigger,
        IReadOnlyDictionary<string, RuleInputMapping> inputMappings,
        int priority,
        RuleBindingFailureBehavior failureBehavior)
    {
        if (definitionVersion <= 0)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding definition version must be positive.");
        if (!SegmentPattern().IsMatch(targetType?.Trim() ?? string.Empty))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding target type is invalid.");
        if (string.IsNullOrWhiteSpace(targetId) || targetId.Trim().Length > 200)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding target ID is required and cannot exceed 200 characters.");
        if (!SegmentPattern().IsMatch(useCaseOrTrigger?.Trim() ?? string.Empty))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding use case or trigger is invalid.");
        if (priority < 0)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding priority cannot be negative.");
        if (!Enum.IsDefined(failureBehavior))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding failure behavior is not supported.");
        if (inputMappings is null)
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding input mappings are required.");

        foreach ((string key, RuleInputMapping mapping) in inputMappings)
        {
            if (!InputKeyPattern().IsMatch(key) || mapping is null)
                return Result.Failure(ErrorCodes.InvalidInput, "Rule binding input mapping is invalid.");
        }
        return Result.Success();
    }

    private static Dictionary<string, RuleInputMapping> Clone(IReadOnlyDictionary<string, RuleInputMapping> mappings) =>
        mappings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex InputKeyPattern();
}
