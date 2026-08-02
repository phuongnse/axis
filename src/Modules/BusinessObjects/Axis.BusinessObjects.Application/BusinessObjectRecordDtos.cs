using Axis.BusinessObjects.Domain.Aggregates;

namespace Axis.BusinessObjects.Application;

public sealed record BusinessObjectRecordFieldContractDto(
    string FieldKey,
    string Label,
    int Order,
    BusinessObjectFieldType FieldType,
    BusinessObjectDefinitionVersionChoiceFieldConfigurationDto? ChoiceConfiguration,
    IReadOnlyList<BusinessObjectDefinitionVersionFieldRuleDto> Rules);

public sealed record BusinessObjectRecordRuleDiagnosticDto(
    string NodeId,
    bool IsMatch);

public sealed record BusinessObjectRecordRuleEvaluationDto(
    string FieldKey,
    Guid BindingId,
    int BindingRevision,
    string DefinitionKey,
    int DefinitionVersion,
    bool IsMatch,
    IReadOnlyList<BusinessObjectRecordRuleDiagnosticDto> Diagnostics);

public sealed record BusinessObjectRecordDetailDto(
    Guid Id,
    Guid WorkspaceId,
    string ObjectKey,
    int DefinitionVersion,
    Guid DefinitionVersionId,
    BusinessObjectRecordStatus Status,
    int Revision,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    IReadOnlyList<BusinessObjectRecordFieldContractDto> Fields,
    IReadOnlyList<BusinessObjectRecordRuleEvaluationDto> RuleEvaluations,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    Guid UpdatedByUserId,
    DateTime UpdatedAt,
    Guid? SubmittedByUserId,
    DateTime? SubmittedAt);

public sealed record BusinessObjectRecordListItemDto(
    Guid Id,
    string ObjectKey,
    int DefinitionVersion,
    BusinessObjectRecordStatus Status,
    int Revision,
    DateTime UpdatedAt,
    DateTime? SubmittedAt);

public sealed record BusinessObjectRecordSubmitResultDto(
    bool IsSubmitted,
    BusinessObjectRecordDetailDto Record,
    IReadOnlyList<BusinessObjectRecordRuleEvaluationDto> RuleEvaluations);

public sealed record CreateBusinessObjectRecordRequest(
    string IdempotencyKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Values = null);

public sealed record SaveBusinessObjectRecordRequest(
    int ExpectedRevision,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values);

public sealed record SubmitBusinessObjectRecordRequest(int ExpectedRevision);
