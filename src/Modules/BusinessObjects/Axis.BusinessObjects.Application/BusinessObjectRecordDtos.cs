using System.ComponentModel.DataAnnotations;
using Axis.BusinessObjects.Domain.Aggregates;

namespace Axis.BusinessObjects.Application;

public sealed record BusinessObjectRecordFieldContractDto(
    [property: Required]
    string FieldKey,
    [property: Required]
    string Label,
    [property: Required]
    int Order,
    [property: Required]
    BusinessObjectFieldType FieldType,
    BusinessObjectDefinitionVersionChoiceFieldConfigurationDto? ChoiceConfiguration,
    [property: Required]
    IReadOnlyList<BusinessObjectDefinitionVersionFieldRuleDto> Rules);

public sealed record BusinessObjectRecordRuleDiagnosticDto(
    [property: Required]
    string NodeId,
    [property: Required]
    bool IsMatch);

public sealed record BusinessObjectRecordRuleEvaluationDto(
    [property: Required]
    string FieldKey,
    [property: Required]
    Guid BindingId,
    [property: Required]
    int BindingRevision,
    [property: Required]
    string DefinitionKey,
    [property: Required]
    int DefinitionVersion,
    [property: Required]
    bool IsMatch,
    [property: Required]
    IReadOnlyList<BusinessObjectRecordRuleDiagnosticDto> Diagnostics);

public sealed record BusinessObjectRecordDetailDto(
    [property: Required]
    Guid Id,
    [property: Required]
    Guid WorkspaceId,
    [property: Required]
    string ObjectKey,
    [property: Required]
    int DefinitionVersion,
    [property: Required]
    Guid DefinitionVersionId,
    [property: Required]
    BusinessObjectRecordStatus Status,
    [property: Required]
    int Revision,
    [property: Required]
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    [property: Required]
    IReadOnlyList<BusinessObjectRecordFieldContractDto> Fields,
    [property: Required]
    IReadOnlyList<BusinessObjectRecordRuleEvaluationDto> RuleEvaluations,
    [property: Required]
    Guid CreatedByUserId,
    [property: Required]
    DateTime CreatedAt,
    [property: Required]
    Guid UpdatedByUserId,
    [property: Required]
    DateTime UpdatedAt,
    Guid? SubmittedByUserId,
    DateTime? SubmittedAt);

public sealed record BusinessObjectRecordListItemDto(
    [property: Required]
    Guid Id,
    [property: Required]
    string ObjectKey,
    [property: Required]
    int DefinitionVersion,
    [property: Required]
    BusinessObjectRecordStatus Status,
    [property: Required]
    int Revision,
    [property: Required]
    DateTime UpdatedAt,
    DateTime? SubmittedAt);

public sealed record BusinessObjectRecordSubmitResultDto(
    [property: Required]
    bool IsSubmitted,
    [property: Required]
    BusinessObjectRecordDetailDto Record,
    [property: Required]
    IReadOnlyList<BusinessObjectRecordRuleEvaluationDto> RuleEvaluations);

public sealed record CreateBusinessObjectRecordRequest(
    [property: Required]
    string IdempotencyKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Values = null);

public sealed record SaveBusinessObjectRecordRequest(
    [property: Required]
    int ExpectedRevision,
    [property: Required]
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values);

public sealed record SubmitBusinessObjectRecordRequest([property: Required] int ExpectedRevision);
