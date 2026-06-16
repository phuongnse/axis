using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetRecords;

/// <summary>/043: Paginated list of records for a model, with optional text search, per-field filters and sort.</summary>
public sealed record GetRecordsQuery(
    Guid ModelId,
    Guid TeamAccountId,
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    IReadOnlyList<RecordFilter>? Filters = null,
    string? SortBy = null,
    string? SortDir = null) : IQuery<Result<RecordsPageDto>>;
