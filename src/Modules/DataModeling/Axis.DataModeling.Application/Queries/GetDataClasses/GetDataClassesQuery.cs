using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClasses;

/// <summary>US-037: Returns a paginated list of data classes for an organization.</summary>
public sealed record GetDataClassesQuery(Guid OrganizationId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<DataClassSummaryDto>>;
