using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.DataModeling.Application.Queries.GetModel;

public sealed record ModelDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FieldDefinitionDto> Fields);
