using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Repositories;

namespace Axis.WorkflowEngine.Application.Queries.GetRetryHistory;

public sealed class GetRetryHistoryHandler(IExecutionRepository execRepo)
    : IQueryHandler<GetRetryHistoryQuery, IReadOnlyList<ExecutionSummaryResponse>>
{
    public async Task<IReadOnlyList<ExecutionSummaryResponse>> Handle(
        GetRetryHistoryQuery query, CancellationToken cancellationToken)
        => await execRepo.GetRetriesAsync(query.OriginalExecutionId, query.tenantId, cancellationToken);
}
