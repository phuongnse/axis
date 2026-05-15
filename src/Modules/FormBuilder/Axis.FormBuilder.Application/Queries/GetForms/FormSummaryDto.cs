namespace Axis.FormBuilder.Application.Queries.GetForms;

public sealed record FormSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    int FieldCount,
    DateTimeOffset UpdatedAt);
