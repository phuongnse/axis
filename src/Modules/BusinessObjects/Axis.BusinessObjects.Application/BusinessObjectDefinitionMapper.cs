using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Application;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectDefinitionMapper
{
    public static BusinessObjectDefinitionDetailDto ToDetailDto(
        BusinessObjectDefinition definition,
        bool canManage = false)
    {
        BusinessObjectDefinitionVersion? latestVersion = definition.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();

        return new BusinessObjectDefinitionDetailDto(
            definition.Id.Value,
            definition.WorkspaceId,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.Revision,
            definition.LatestPublishedVersionNumber,
            definition.CreatedAt,
            definition.UpdatedAt,
            ToMetadata(definition),
            definition.Fields.OrderBy(field => field.Order).Select(ToFieldDto).ToArray(),
            latestVersion is null ? null : ToVersionDto(latestVersion),
            new BusinessObjectDefinitionActionsDto(
                CanSave: canManage && !definition.IsInstalled && definition.Status == BusinessObjectDefinitionStatus.Unpublished,
                CanPublish: canManage && !definition.IsInstalled && definition.Status == BusinessObjectDefinitionStatus.Unpublished && definition.Fields.Count > 0));
    }

    public static BusinessObjectDefinitionListItemDto ToListItemDto(BusinessObjectDefinition definition) =>
        new(
            definition.Id.Value,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.Revision,
            definition.LatestPublishedVersionNumber,
            definition.UpdatedAt,
            ToMetadata(definition));

    private static ResourceMetadataDto ToMetadata(BusinessObjectDefinition definition) =>
        new(
            definition.Revision,
            definition.CreatedBy is { } createdBy ? ResourceActorDto.From(createdBy) : null,
            AsOffset(definition.CreatedAt),
            definition.UpdatedBy is { } updatedBy ? ResourceActorDto.From(updatedBy) : null,
            AsOffset(definition.UpdatedAt));

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static BusinessObjectDefinitionVersionDto ToVersionDto(BusinessObjectDefinitionVersion version) =>
        new(
            version.Id.Value,
            version.SourceDefinitionId.Value,
            version.VersionNumber,
            SubjectReferenceMapper.ToDto(version.PublishedBySubject),
            version.PublishedAt,
            version.Fields.OrderBy(field => field.Order).Select(ToVersionFieldDto).ToArray());

    private static BusinessObjectFieldDefinitionDto ToFieldDto(BusinessObjectFieldDefinition field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            ToChoiceDto(field.ChoiceSelectionMode, field.ChoiceOptions),
            field.Rules.OrderBy(rule => rule.Order).Select(ToRuleDto).ToArray());

    private static BusinessObjectDefinitionVersionFieldDto ToVersionFieldDto(
        BusinessObjectDefinitionVersionField field) =>
        new(
            field.Id.Value,
            field.SourceFieldDefinitionId.Value,
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            ToChoiceDto(field.ChoiceSelectionMode, field.ChoiceOptions),
            field.Rules.OrderBy(rule => rule.Order).Select(ToRuleDto).ToArray());

    private static BusinessObjectChoiceFieldConfigurationDto? ToChoiceDto(
        BusinessObjectChoiceSelectionMode? selectionMode,
        IEnumerable<BusinessObjectChoiceOption> options) =>
        selectionMode is null
            ? null
            : new BusinessObjectChoiceFieldConfigurationDto(
                selectionMode.Value,
                options.OrderBy(option => option.Order)
                    .Select(option => new BusinessObjectChoiceOptionDto(
                        option.Id.Value,
                        option.Key.Value,
                        option.Label,
                        option.Order))
                    .ToArray());

    private static BusinessObjectDefinitionVersionChoiceFieldConfigurationDto? ToChoiceDto(
        BusinessObjectChoiceSelectionMode? selectionMode,
        IEnumerable<BusinessObjectDefinitionVersionChoiceOption> options) =>
        selectionMode is null
            ? null
            : new BusinessObjectDefinitionVersionChoiceFieldConfigurationDto(
                selectionMode.Value,
                options.OrderBy(option => option.Order)
                    .Select(option => new BusinessObjectDefinitionVersionChoiceOptionDto(
                        option.Id.Value,
                        option.SourceChoiceOptionId.Value,
                        option.Key.Value,
                        option.Label,
                        option.Order))
                    .ToArray());

    private static BusinessObjectFieldRuleDto ToRuleDto(BusinessObjectFieldRule rule) =>
        new(
            rule.Id.Value,
            rule.BindingId,
            rule.BindingRevision,
            rule.Order);

    private static BusinessObjectDefinitionVersionFieldRuleDto ToRuleDto(
        BusinessObjectDefinitionVersionFieldRule rule) =>
        new(
            rule.Id.Value,
            rule.SourceFieldRuleId.Value,
            rule.BindingId,
            rule.BindingRevision,
            rule.Order);
}
