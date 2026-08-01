namespace Axis.Rules.Contracts;

public sealed record RuleApplicationValidationRequest(
    Guid WorkspaceId,
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Inputs,
    IReadOnlyDictionary<string, RuleValueType>? InputTypes = null);

public sealed record RuleApplicationValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? Error,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? CanonicalInputs)
{
    public static RuleApplicationValidationResult Valid(
        IReadOnlyDictionary<string, IReadOnlyList<string>> canonicalInputs) =>
        new(true, null, null, canonicalInputs);

    public static RuleApplicationValidationResult Invalid(string errorCode, string error) =>
        new(false, errorCode, error, null);
}

public sealed record RuleEvaluationReference(
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Inputs,
    IReadOnlyDictionary<string, RuleValueType>? InputTypes = null);

public sealed record RuleEvaluationRequest(
    Guid WorkspaceId,
    IReadOnlyList<RuleEvaluationReference> Rules,
    string CorrelationId);

public sealed record RuleNodeDiagnosticDto(string NodeId, bool IsMatch);

public sealed record RuleEvaluationItemDto(
    string DefinitionKey,
    int DefinitionVersion,
    bool IsMatch,
    IReadOnlyList<RuleNodeDiagnosticDto> Diagnostics);

public sealed record RuleEvaluationResult(
    bool IsSuccess,
    IReadOnlyList<RuleEvaluationItemDto> Items,
    string CorrelationId,
    string? ErrorCode,
    string? Error);

public sealed record RuleContextValue(
    RuleValueType Type,
    IReadOnlyList<string> Values);

public sealed record RuleContext(
    IReadOnlyDictionary<string, RuleContextValue> Values);

public interface IRuleContextAdapter<in TConsumerContext>
{
    string TargetType { get; }
    RuleContext CreateContext(TConsumerContext consumerContext);
}

public sealed record RuleBindingEvaluationRequest(
    Guid WorkspaceId,
    Guid BindingId,
    RuleContext Context,
    string CorrelationId);

public interface IRuleBindingEvaluator
{
    Task<RuleEvaluationResult> EvaluateBindingAsync(
        RuleBindingEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRuleApplicationValidator
{
    Task<RuleApplicationValidationResult> ValidateAsync(
        RuleApplicationValidationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRuleEvaluator
{
    Task<RuleEvaluationResult> EvaluateAsync(
        RuleEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
