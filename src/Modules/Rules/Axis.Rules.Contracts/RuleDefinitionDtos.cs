namespace Axis.Rules.Contracts;

public sealed record RuleValueDto(
    RuleValueType Type,
    IReadOnlyList<string> Values);

public sealed record RuleInputDefinitionDto(
    string Key,
    string Label,
    IReadOnlyList<RuleValueType> Types,
    bool IsRequired,
    bool AllowMultiple,
    IReadOnlyList<string> AllowedValues);

public sealed record RuleDraftInputDefinitionDto(
    string Label,
    IReadOnlyList<RuleValueType> Types,
    bool IsRequired,
    bool AllowMultiple,
    IReadOnlyList<string> AllowedValues);

public sealed record RuleOutputContractDto(
    RuleValueType Type,
    RuleExpressionCardinality Cardinality);

public sealed record RuleDefinitionSummaryDto(
    string DefinitionKey,
    string Name,
    string Description,
    RuleOrigin Origin,
    RuleLifecycleStatus Status,
    int ExpressionLanguageVersion,
    int? Revision,
    int? LatestPublishedVersion,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    RuleOutputContractDto Output,
    DateTime? UpdatedAt,
    RuleReferenceDocumentationDto? Documentation = null);

public sealed record RuleDefinitionDetailDto(
    string DefinitionKey,
    string Name,
    string Description,
    RuleOrigin Origin,
    RuleLifecycleStatus Status,
    int ExpressionLanguageVersion,
    int? Revision,
    int? LatestPublishedVersion,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    RuleOutputContractDto Output,
    RuleConditionNodeDto? Condition,
    IReadOnlyList<RuleDefinitionVersionDto> Versions,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? ArchivedAt,
    RuleReferenceDocumentationDto? Documentation = null);

public sealed record RuleDefinitionVersionDto(
    int Version,
    string Name,
    string Description,
    int ExpressionLanguageVersion,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    RuleOutputContractDto Output,
    RuleConditionNodeDto Condition,
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
