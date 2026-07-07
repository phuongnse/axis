using Axis.Objects.Domain.Aggregates;

namespace Axis.Objects.Application;

public sealed record ObjectFieldDefinitionDto(
    Guid Id,
    string FieldKey,
    string Label,
    int Order);

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
    int DraftVersion,
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
    int DraftVersion,
    int? LatestPublishedVersionNumber,
    DateTime UpdatedAt);

public sealed record ObjectFieldDefinitionInput(
    string FieldKey,
    string Label);
