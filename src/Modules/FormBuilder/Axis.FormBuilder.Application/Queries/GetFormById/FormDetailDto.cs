namespace Axis.FormBuilder.Application.Queries.GetFormById;

public sealed record FormDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FormFieldDto> Fields);
