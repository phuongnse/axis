using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModels;

/// <summary>Lists non-deleted models for the Workspace, paginated.</summary>
public sealed class GetModelsHandler(IDataModelRepository modelRepo)
    : IQueryHandler<GetModelsQuery, PagedResult<ModelSummaryDto>>
{
    public async Task<PagedResult<ModelSummaryDto>> Handle(GetModelsQuery query, CancellationToken cancellationToken)
    {
        int effectivePageSize = Math.Min(query.PageSize, 100);

        (IReadOnlyList<DataModeling.Domain.Aggregates.DataModel> items, int totalCount) =
            await modelRepo.GetPagedAsync(query.workspaceId, query.Page, effectivePageSize, cancellationToken);

        IReadOnlyList<ModelSummaryDto> dtos = items
            .Select(m => new ModelSummaryDto(m.Id, m.Name, m.Description, m.Icon, m.Color, m.Fields.Count, m.UpdatedAt))
            .ToList();

        return new PagedResult<ModelSummaryDto>(dtos, totalCount, query.Page, effectivePageSize);
    }
}
