using System.ComponentModel.DataAnnotations;
using Axis.Identity.Contracts;

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
    string Key,
    string Label,
    IReadOnlyList<RuleValueType> Types,
    bool IsRequired,
    bool AllowMultiple,
    IReadOnlyList<string> AllowedValues);

public sealed record RuleOutputContractDto(
    RuleValueType Type,
    RuleExpressionCardinality Cardinality);

public sealed record RuleResourceActorDto(
    [property: Required] string Kind,
    Guid? SubjectId,
    [property: Required] string DisplayName);

public sealed record RuleResourceMetadataDto(
    long? Revision,
    [property: Required] RuleResourceActorDto CreatedBy,
    [property: Required] DateTimeOffset CreatedAt,
    [property: Required] RuleResourceActorDto ModifiedBy,
    [property: Required] DateTimeOffset ModifiedAt);

public sealed record RuleDefinitionSummaryDto(
    string DefinitionKey,
    string Name,
    string Description,
    RuleOrigin Origin,
    RuleLifecycleStatus Status,
    int ExpressionLanguageVersion,
    int? Revision,
    int? LatestVersion,
    int? ActiveVersion,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    RuleOutputContractDto Output,
    DateTime UpdatedAt,
    [property: Required] RuleResourceMetadataDto Metadata,
    RuleDefinitionActionsDto Actions,
    RuleReferenceDocumentationDto? Documentation = null);

public sealed record RuleDefinitionDetailDto(
    string DefinitionKey,
    string Name,
    string Description,
    RuleOrigin Origin,
    RuleLifecycleStatus Status,
    int ExpressionLanguageVersion,
    int? Revision,
    int? LatestVersion,
    int? ActiveVersion,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    RuleOutputContractDto Output,
    RuleConditionNodeDto? Condition,
    IReadOnlyList<RuleDefinitionVersionDto> Versions,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ArchivedAt,
    [property: Required] RuleResourceMetadataDto Metadata,
    RuleDefinitionActionsDto Actions,
    RuleReferenceDocumentationDto? Documentation = null);

public sealed record RuleDefinitionActionsDto(
    bool CanEditDraft,
    bool CanCreateVersion,
    bool CanActivateVersion,
    bool CanDeactivate,
    bool CanArchive);

public sealed record RuleDefinitionCollectionActionsDto(bool CanStartCreate);

public sealed record RuleDefinitionVersionDto(
    int Version,
    string Name,
    string Description,
    int ExpressionLanguageVersion,
    IReadOnlyList<RuleInputDefinitionDto> Inputs,
    RuleOutputContractDto Output,
    RuleConditionNodeDto Condition,
    SubjectReferenceDto? PublishedBySubject,
    DateTime CreatedAt);

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
