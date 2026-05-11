using Axis.DataModeling.Application.Queries.GetModel;

namespace Axis.DataModeling.Application.Queries.GetDataClass;

public sealed record DataClassDetailDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FieldDefinitionDto> Fields);
