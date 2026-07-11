namespace Axis.Rules.Contracts;

public sealed record RuleApplicationTarget(
    RuleScope Scope,
    string ContextKey,
    int ContextSchemaVersion,
    string TargetTypeKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Configuration);

public sealed record RuleApplicationValidationRequest(
    Guid WorkspaceId,
    string DefinitionKey,
    int DefinitionVersion,
    RuleApplicationTarget Target,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Parameters);

public sealed record RuleApplicationValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? Error,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? CanonicalParameters)
{
    public static RuleApplicationValidationResult Valid(
        IReadOnlyDictionary<string, IReadOnlyList<string>> canonicalParameters) =>
        new(true, null, null, canonicalParameters);
    public static RuleApplicationValidationResult Invalid(string errorCode, string error) =>
        new(false, errorCode, error, null);
}

public sealed record RuleEvaluationReference(
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Parameters);

public sealed record RuleEvaluationRequest(
    Guid WorkspaceId,
    RuleOutcomeKind Purpose,
    RuleScope Scope,
    string ContextKey,
    int ContextSchemaVersion,
    IReadOnlyList<RuleEvaluationReference> Rules,
    IReadOnlyDictionary<string, RuleValueDto> Context,
    string CorrelationId);

public sealed record RuleViolationDto(
    string DefinitionKey,
    int DefinitionVersion,
    string Code,
    RuleSeverity Severity,
    string Message);

public sealed record RuleEvaluationItemDto(
    string DefinitionKey,
    int DefinitionVersion,
    bool IsMatch,
    RuleOutcomeDto? Outcome);

public sealed record RuleEvaluationResult(
    bool IsSuccess,
    bool IsAllowed,
    int ContextSchemaVersion,
    IReadOnlyList<RuleViolationDto> Violations,
    IReadOnlyList<RuleEvaluationItemDto> Items,
    string CorrelationId,
    string? ErrorCode,
    string? Error);

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

public interface IRuleContextSchemaProvider
{
    Task<IReadOnlyList<RuleContextSchemaDto>> ListSchemasAsync(
        Guid workspaceId,
        RuleScope? scope = null,
        CancellationToken cancellationToken = default);

    Task<RuleContextSchemaDto?> FindSchemaAsync(
        Guid workspaceId,
        string contextKey,
        int version,
        CancellationToken cancellationToken = default);
}
