using Axis.Objects.Domain.Aggregates;

namespace Axis.Objects.Application;

public sealed record ObjectFieldDefinitionDto(
    Guid Id,
    string FieldKey,
    string Label,
    int Order,
    ObjectFieldType FieldType,
    IReadOnlyList<ObjectFieldVariantDto> Variants);

public sealed record ObjectFieldVariantDto(
    ObjectFieldVariantKind Kind,
    decimal? MinNumber,
    decimal? MaxNumber,
    DateOnly? MinDate,
    DateOnly? MaxDate,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    IReadOnlyList<string> Options);

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
    IReadOnlyList<ObjectFieldVariantInput>? Variants = null);

public sealed record ObjectFieldVariantInput(
    ObjectFieldVariantKind Kind,
    decimal? MinNumber = null,
    decimal? MaxNumber = null,
    DateOnly? MinDate = null,
    DateOnly? MaxDate = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    IReadOnlyList<string>? Options = null);
