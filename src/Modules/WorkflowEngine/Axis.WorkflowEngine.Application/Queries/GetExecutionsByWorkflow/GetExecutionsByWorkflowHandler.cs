using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Repositories;

namespace Axis.WorkflowEngine.Application.Queries.GetExecutionsByWorkflow;

public sealed class GetExecutionsByWorkflowHandler(IExecutionRepository execRepo)
    : IQueryHandler<GetExecutionsByWorkflowQuery, PagedResult<ExecutionSummaryResponse>>
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<ExecutionSummaryResponse>> Handle(
        GetExecutionsByWorkflowQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        (IReadOnlyList<ExecutionSummaryResponse> items, int total) = await execRepo.GetPagedByWorkflowAsync(
            query.WorkflowDefinitionId, query.tenantId, page, pageSize, query.Status, cancellationToken);

        return new PagedResult<ExecutionSummaryResponse>(items, total, page, pageSize);
    }
}
