using Axis.BusinessObjects.Domain.Aggregates;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRecordMapper
{
    public static BusinessObjectRecordDetailDto ToDetailDto(
        BusinessObjectRecord record,
        BusinessObjectDefinitionVersion definition) =>
        new(
            record.Id.Value,
            record.WorkspaceId,
            record.ObjectKey.Value,
            record.DefinitionVersionNumber,
            record.DefinitionVersionId.Value,
            record.Status,
            record.Revision,
            record.Values,
            definition.Fields.OrderBy(field => field.Order).Select(ToFieldContractDto).ToArray(),
            record.RuleEvaluations.Select(ToEvaluationDto).ToArray(),
            record.CreatedByUserId,
            record.CreatedAt,
            record.UpdatedByUserId,
            record.UpdatedAt,
            record.SubmittedByUserId,
            record.SubmittedAt);

    public static BusinessObjectRecordListItemDto ToListItemDto(BusinessObjectRecord record) =>
        new(
            record.Id.Value,
            record.ObjectKey.Value,
            record.DefinitionVersionNumber,
            record.Status,
            record.Revision,
            record.UpdatedAt,
            record.SubmittedAt);

    public static BusinessObjectRecordRuleEvaluationDto ToEvaluationDto(
        BusinessObjectRecordRuleEvaluation evaluation) =>
        new(
            evaluation.FieldKey,
            evaluation.BindingId,
            evaluation.BindingRevision,
            evaluation.DefinitionKey,
            evaluation.DefinitionVersion,
            evaluation.IsMatch,
            evaluation.Diagnostics
                .Select(diagnostic => new BusinessObjectRecordRuleDiagnosticDto(
                    diagnostic.NodeId,
                    diagnostic.IsMatch))
                .ToArray());

    private static BusinessObjectRecordFieldContractDto ToFieldContractDto(
        BusinessObjectDefinitionVersionField field) =>
        new(
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            field.ChoiceSelectionMode is null
                ? null
                : new BusinessObjectDefinitionVersionChoiceFieldConfigurationDto(
                    field.ChoiceSelectionMode.Value,
                    field.ChoiceOptions
                        .OrderBy(option => option.Order)
                        .Select(option => new BusinessObjectDefinitionVersionChoiceOptionDto(
                            option.Id.Value,
                            option.SourceChoiceOptionId.Value,
                            option.Key.Value,
                            option.Label,
                            option.Order))
                        .ToArray()),
            field.Rules
                .OrderBy(rule => rule.Order)
                .Select(rule => new BusinessObjectDefinitionVersionFieldRuleDto(
                    rule.Id.Value,
                    rule.SourceFieldRuleId.Value,
                    rule.BindingId,
                    rule.BindingRevision,
                    rule.Order))
                .ToArray());
}
