using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClasses;

/// <summary>Lists non-deleted data classes for the team account, paginated.</summary>
public sealed class GetDataClassesHandler(IDataClassRepository dataClassRepo)
    : IQueryHandler<GetDataClassesQuery, PagedResult<DataClassSummaryDto>>
{
    public async Task<PagedResult<DataClassSummaryDto>> Handle(
        GetDataClassesQuery query, CancellationToken cancellationToken)
    {
        int effectivePageSize = Math.Min(query.PageSize, 100);

        (IReadOnlyList<DataClass> items, int totalCount) =
            await dataClassRepo.GetPagedAsync(query.TeamAccountId, query.Page, effectivePageSize, cancellationToken);

        IReadOnlyList<DataClassSummaryDto> dtos = items
            .Select(c => new DataClassSummaryDto(c.Id, c.Name, c.Description, c.Fields.Count, c.CreatedAt))
            .ToList();

        return new PagedResult<DataClassSummaryDto>(dtos, totalCount, query.Page, effectivePageSize);
    }
}
