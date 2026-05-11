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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, object?> Data);
