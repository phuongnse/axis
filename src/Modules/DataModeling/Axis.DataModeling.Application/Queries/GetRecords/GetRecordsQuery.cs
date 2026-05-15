using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetRecords;

/// <summary>US-042/043: Paginated list of records for a model, with optional text search.</summary>
public sealed record GetRecordsQuery(
    Guid ModelId,
    Guid OrganizationId,
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    string[]? Filters = null,
    string? SortBy = null,
    bool? SortDesc = null) : IQuery<Result<RecordsPageDto>>;
