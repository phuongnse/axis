namespace Axis.Rules.Contracts;

public enum RuleInputMappingKind
{
    Context = 0,
    Literal = 1,
}

public enum RuleBindingFailureBehavior
{
    FailClosed = 0,
    FailOpen = 1,
}

public sealed record RuleInputMappingDto(
    RuleInputMappingKind Kind,
    string? ContextKey,
    IReadOnlyList<string> LiteralValues);

public sealed record RuleBindingDto(
    Guid Id,
    Guid WorkspaceId,
    string DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleInputMappingDto> InputMappings,
    int Priority,
    bool Enabled,
    RuleBindingFailureBehavior FailureBehavior,
    int Revision,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record RuleBindingUsageDto(
    Guid BindingId,
    string DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    int Priority,
    bool Enabled,
    RuleBindingFailureBehavior FailureBehavior,
    int Revision);

public sealed record CreateRuleBindingRequest(
    string DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleInputMappingDto> InputMappings,
    int Priority = 0,
    bool Enabled = true,
    RuleBindingFailureBehavior FailureBehavior = RuleBindingFailureBehavior.FailClosed);

public sealed record UpdateRuleBindingRequest(
    int ExpectedRevision,
    string DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleInputMappingDto> InputMappings,
    int Priority = 0,
    bool Enabled = true,
    RuleBindingFailureBehavior FailureBehavior = RuleBindingFailureBehavior.FailClosed);

public sealed record DeleteRuleBindingRequest(int ExpectedRevision);

public sealed record EvaluateRuleBindingRequest(
    RuleContext Context,
    int? BindingRevision = null);

public sealed record RuleBindingReferenceValidationResult(
    bool IsValid,
    Guid? BindingId,
    int? Revision,
    string? ErrorCode,
    string? Error)
{
    public static RuleBindingReferenceValidationResult Valid(Guid bindingId, int revision) =>
        new(true, bindingId, revision, null, null);

    public static RuleBindingReferenceValidationResult Invalid(string errorCode, string error) =>
        new(false, null, null, errorCode, error);
}

public sealed record RuleBindingContextValueSchema(RuleValueType Type, bool AllowMultiple);

public sealed record RuleBindingReferenceValidationRequest(
    Guid WorkspaceId,
    Guid BindingId,
    string ExpectedTargetType,
    string ExpectedTargetId,
    string ExpectedUseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleBindingContextValueSchema> ContextValues,
    IReadOnlyList<string> RequiredContextKeys,
    int? ExpectedBindingRevision = null);

public interface IRuleBindingReferenceValidator
{
    Task<RuleBindingReferenceValidationResult> ValidateAsync(
        RuleBindingReferenceValidationRequest request,
        CancellationToken cancellationToken = default);
}
