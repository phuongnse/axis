namespace Axis.DataModeling.Application.Queries.GetDataClasses;

public sealed record DataClassSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    int FieldCount,
    DateTime CreatedAt);
