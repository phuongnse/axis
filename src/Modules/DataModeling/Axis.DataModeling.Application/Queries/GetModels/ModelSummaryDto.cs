namespace Axis.DataModeling.Application.Queries.GetModels;

public sealed record ModelSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    int FieldCount,
    DateTime UpdatedAt);
