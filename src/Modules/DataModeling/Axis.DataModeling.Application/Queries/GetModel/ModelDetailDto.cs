using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.DataModeling.Application.Queries.GetModel;

public sealed record ModelDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<FieldDefinitionDto> Fields);

public sealed record FieldDefinitionDto(
    Guid Id,
    string Name,
    string Label,
    string? HelpText,
    string Type,
    bool IsRequired,
    bool IsSystem,
    int DisplayOrder,
    FieldConfig Config);
