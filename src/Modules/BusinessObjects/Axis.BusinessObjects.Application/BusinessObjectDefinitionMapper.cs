using Axis.BusinessObjects.Domain.Aggregates;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectDefinitionMapper
{
    public static BusinessObjectDefinitionDetailDto ToDetailDto(BusinessObjectDefinition definition)
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
            definition.Fields.OrderBy(field => field.Order).Select(ToFieldDto).ToArray(),
            latestVersion is null ? null : ToVersionDto(latestVersion));
    }

    public static BusinessObjectDefinitionListItemDto ToListItemDto(BusinessObjectDefinition definition) =>
        new(
            definition.Id.Value,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.Revision,
            definition.LatestPublishedVersionNumber,
            definition.UpdatedAt);

    public static BusinessObjectDefinitionVersionDto ToVersionDto(BusinessObjectDefinitionVersion version) =>
        new(
            version.Id.Value,
            version.SourceDefinitionId.Value,
            version.VersionNumber,
            version.PublishedByUserId,
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
            rule.DefinitionKey,
            rule.DefinitionVersion,
            ToParameterDto(rule.Parameters));

    private static BusinessObjectDefinitionVersionFieldRuleDto ToRuleDto(
        BusinessObjectDefinitionVersionFieldRule rule) =>
        new(
            rule.Id.Value,
            rule.SourceFieldRuleId.Value,
            rule.DefinitionKey,
            rule.DefinitionVersion,
            ToParameterDto(rule.Parameters));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToParameterDto(
        IReadOnlyDictionary<string, string[]> parameters) =>
        parameters.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
}
