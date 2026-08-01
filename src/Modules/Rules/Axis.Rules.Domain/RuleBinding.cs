using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial class RuleBinding : AggregateRoot<RuleBindingId>
{
    private Dictionary<string, RuleInputMapping> _inputMappings = new(StringComparer.Ordinal);

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
        Guid createdByUserId,
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
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
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
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

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
        Guid createdByUserId,
        DateTime createdAt)
    {
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(definitionKey.Value);
        if (workspaceId == Guid.Empty || createdByUserId == Guid.Empty)
            return Result.Failure<RuleBinding>(ErrorCodes.InvalidInput, "Workspace and acting user are required.");
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
                createdByUserId,
                createdAt);
    }

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
        Guid updatedByUserId,
        DateTime updatedAt)
    {
        if (updatedByUserId == Guid.Empty)
            return Result.Failure(ErrorCodes.InvalidInput, "Acting user is required.");
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
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
        return Result.Success();
    }

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
