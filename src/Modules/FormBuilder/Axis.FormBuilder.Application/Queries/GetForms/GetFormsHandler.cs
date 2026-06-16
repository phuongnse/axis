using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetForms;

public sealed class GetFormsHandler(IFormRepository formRepo)
    : IQueryHandler<GetFormsQuery, PagedResult<FormSummaryDto>>
{
    public async Task<PagedResult<FormSummaryDto>> Handle(GetFormsQuery query, CancellationToken cancellationToken)
    {
        int effectivePage = Math.Max(1, query.Page);
        int effectivePageSize = Math.Min(Math.Max(1, query.PageSize), 100);

        IReadOnlyList<FormDefinition> all = await formRepo.GetAllAsync(query.workspaceId, cancellationToken);

        int totalCount = all.Count;
        IReadOnlyList<FormSummaryDto> page = all
            .OrderByDescending(f => f.UpdatedAt)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(f => new FormSummaryDto(f.Id, f.Name, f.Description, f.Fields.Count, f.UpdatedAt))
            .ToList();

        return new PagedResult<FormSummaryDto>(page, totalCount, effectivePage, effectivePageSize);
    }
}
