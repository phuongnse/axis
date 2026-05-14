namespace Axis.DataModeling.Application.Queries.GetRecords;

public sealed record RecordsPageDto(
    IReadOnlyList<RecordDto> Records,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
