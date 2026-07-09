using Axis.Objects.Domain.Aggregates;

namespace Axis.Objects.Application;

public sealed record ObjectFieldDefinitionDto(
    Guid Id,
    string FieldKey,
    string Label,
    int Order,
    ObjectFieldType FieldType,
    IReadOnlyList<ObjectFieldRuleDto> Rules);

public sealed record ObjectFieldRuleDto(
    string DefinitionKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Parameters);

public sealed record ObjectDefinitionVersionDto(
    Guid Id,
    int VersionNumber,
    Guid PublishedByUserId,
    DateTime PublishedAt,
    IReadOnlyList<ObjectFieldDefinitionDto> Fields);

public sealed record ObjectDefinitionDetailDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string ObjectKey,
    ObjectDefinitionStatus Status,
    int Revision,
    int? LatestPublishedVersionNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ObjectFieldDefinitionDto> Fields,
    ObjectDefinitionVersionDto? LatestPublishedVersion);

public sealed record ObjectDefinitionListItemDto(
    Guid Id,
    string Name,
    string ObjectKey,
    ObjectDefinitionStatus Status,
    int Revision,
    int? LatestPublishedVersionNumber,
    DateTime UpdatedAt);

public sealed record ObjectFieldDefinitionInput(
    string FieldKey,
    string Label,
    ObjectFieldType FieldType = ObjectFieldType.Text,
    IReadOnlyList<ObjectFieldRuleInput>? Rules = null);

public sealed record ObjectFieldRuleInput(
    string DefinitionKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Parameters = null);
