namespace Axis.DataModeling.Application.Queries.GetRecords;

public sealed record RecordsPageDto(
    IReadOnlyList<RecordDto> Records,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record RecordDto(
    Guid Id,
    Guid ModelId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyDictionary<string, object?> Data);
