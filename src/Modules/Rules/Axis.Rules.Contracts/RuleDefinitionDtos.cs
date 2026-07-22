namespace Axis.Rules.Contracts;

public sealed record RuleValueDto(
    RuleValueType Type,
    IReadOnlyList<string> Values);

public sealed record RuleParameterDefinitionDto(
    string Key,
    RuleValueType Type,
    bool IsRequired,
    bool AllowMultiple,
    IReadOnlyList<string> AllowedValues);

public sealed record RuleApplicabilityDto(
    IReadOnlyList<string> TargetTypeKeys,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ConfigurationConstraints);

public sealed record RuleDefinitionSummaryDto(
    string DefinitionKey,
    string Name,
    string Description,
    RuleOrigin Origin,
    RuleScope Scope,
    RuleOutcomeKind OutcomeKind,
    RuleLifecycleStatus Status,
    int ExpressionLanguageVersion,
    int? Revision,
    int? LatestPublishedVersion,
    string? ContextKey,
    int? ContextSchemaVersion,
    RuleApplicabilityDto? Applicability,
    IReadOnlyList<RuleParameterDefinitionDto> Parameters,
    DateTime? UpdatedAt);

public sealed record RuleDefinitionDetailDto(
    string DefinitionKey,
    string Name,
    string Description,
    RuleOrigin Origin,
    RuleScope Scope,
    RuleOutcomeKind OutcomeKind,
    RuleLifecycleStatus Status,
    int ExpressionLanguageVersion,
    int? Revision,
    int? LatestPublishedVersion,
    string? ContextKey,
    int? ContextSchemaVersion,
    RuleApplicabilityDto? Applicability,
    IReadOnlyList<RuleParameterDefinitionDto> Parameters,
    RuleConditionNodeDto? Condition,
    RuleOutcomeDto? Outcome,
    IReadOnlyList<RuleDefinitionVersionDto> Versions,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? ArchivedAt);

public sealed record RuleDefinitionVersionDto(
    int Version,
    string Name,
    string Description,
    RuleScope Scope,
    RuleOutcomeKind OutcomeKind,
    int ExpressionLanguageVersion,
    string ContextKey,
    int ContextSchemaVersion,
    IReadOnlyList<RuleParameterDefinitionDto> Parameters,
    RuleConditionNodeDto Condition,
    RuleOutcomeDto Outcome,
    Guid PublishedByUserId,
    DateTime PublishedAt);

public sealed record RuleConditionNodeDto(
    string NodeId,
    RuleLogicalOperator? LogicalOperator,
    RulePredicateOperator? PredicateOperator,
    RuleOperandDto? Left,
    RuleOperandDto? Right,
    IReadOnlyList<RuleConditionNodeDto> Children);

public sealed record RuleOperandDto(
    RuleOperandKind Kind,
    string? Reference,
    RuleValueDto? Literal,
    RuleExpressionFunction? Function = null,
    IReadOnlyList<RuleOperandDto>? Arguments = null);

public sealed record RuleOutcomeDto(
    RuleOutcomeKind Kind,
    string? ViolationCode,
    RuleSeverity? Severity,
    string? Message,
    RuleDecision? Decision);

public sealed record RuleContextFieldDto(
    string Path,
    string DisplayName,
    RuleValueType Type,
    bool AllowMultiple);

public sealed record RuleContextSchemaDto(
    string ContextKey,
    int Version,
    RuleScope Scope,
    string DisplayName,
    IReadOnlyList<RuleContextFieldDto> Fields,
    string? TargetTypeKey = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Configuration = null);
