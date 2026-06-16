using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClasses;

/// <summary>Returns a paginated list of data classes for a team account.</summary>
public sealed record GetDataClassesQuery(Guid TeamAccountId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<DataClassSummaryDto>>;
