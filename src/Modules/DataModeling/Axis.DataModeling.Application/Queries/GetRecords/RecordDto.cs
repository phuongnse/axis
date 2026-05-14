namespace Axis.DataModeling.Application.Queries.GetRecords;

public sealed record RecordDto(
    Guid Id,
    Guid ModelId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, object?> Data);
