using Axis.BusinessObjects.Domain.Aggregates;

namespace Axis.BusinessObjects.Application;

public sealed record BusinessObjectFieldDefinitionDto(
    Guid Id,
    string FieldKey,
    string Label,
    int Order,
    BusinessObjectFieldType FieldType,
    BusinessObjectChoiceFieldConfigurationDto? ChoiceConfiguration,
    IReadOnlyList<BusinessObjectFieldRuleDto> Rules);

public sealed record BusinessObjectFieldRuleDto(
    Guid Id,
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Parameters);

public sealed record BusinessObjectChoiceOptionDto(
    Guid Id,
    string OptionKey,
    string Label,
    int Order);

public sealed record BusinessObjectChoiceFieldConfigurationDto(
    BusinessObjectChoiceSelectionMode SelectionMode,
    IReadOnlyList<BusinessObjectChoiceOptionDto> Options);

public sealed record BusinessObjectDefinitionVersionDto(
    Guid Id,
    Guid SourceDefinitionId,
    int VersionNumber,
    Guid PublishedByUserId,
    DateTime PublishedAt,
    IReadOnlyList<BusinessObjectDefinitionVersionFieldDto> Fields);

public sealed record BusinessObjectDefinitionVersionFieldDto(
    Guid Id,
    Guid SourceFieldDefinitionId,
    string FieldKey,
    string Label,
    int Order,
    BusinessObjectFieldType FieldType,
    BusinessObjectDefinitionVersionChoiceFieldConfigurationDto? ChoiceConfiguration,
    IReadOnlyList<BusinessObjectDefinitionVersionFieldRuleDto> Rules);

public sealed record BusinessObjectDefinitionVersionFieldRuleDto(
    Guid Id,
    Guid SourceFieldRuleId,
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Parameters);

public sealed record BusinessObjectDefinitionVersionChoiceOptionDto(
    Guid Id,
    Guid SourceChoiceOptionId,
    string OptionKey,
    string Label,
    int Order);

public sealed record BusinessObjectDefinitionVersionChoiceFieldConfigurationDto(
    BusinessObjectChoiceSelectionMode SelectionMode,
    IReadOnlyList<BusinessObjectDefinitionVersionChoiceOptionDto> Options);

public sealed record BusinessObjectDefinitionDetailDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string ObjectKey,
    BusinessObjectDefinitionStatus Status,
    int Revision,
    int? LatestPublishedVersionNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<BusinessObjectFieldDefinitionDto> Fields,
    BusinessObjectDefinitionVersionDto? LatestPublishedVersion);

public sealed record BusinessObjectDefinitionListItemDto(
    Guid Id,
    string Name,
    string ObjectKey,
    BusinessObjectDefinitionStatus Status,
    int Revision,
    int? LatestPublishedVersionNumber,
    DateTime UpdatedAt);

public sealed record BusinessObjectFieldDefinitionInput(
    string FieldKey,
    string Label,
    BusinessObjectFieldType FieldType = BusinessObjectFieldType.Text,
    IReadOnlyList<BusinessObjectFieldRuleInput>? Rules = null,
    BusinessObjectChoiceFieldConfigurationInput? ChoiceConfiguration = null,
    Guid? Id = null);

public sealed record BusinessObjectFieldRuleInput(
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Parameters = null,
    Guid? Id = null);

public sealed record BusinessObjectChoiceOptionInput(
    string OptionKey,
    string Label,
    Guid? Id = null);

public sealed record BusinessObjectChoiceFieldConfigurationInput(
    BusinessObjectChoiceSelectionMode SelectionMode,
    IReadOnlyList<BusinessObjectChoiceOptionInput> Options);
