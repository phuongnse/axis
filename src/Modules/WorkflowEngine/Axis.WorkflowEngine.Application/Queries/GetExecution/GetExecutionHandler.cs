using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Repositories;

namespace Axis.WorkflowEngine.Application.Queries.GetExecution;

public sealed class GetExecutionHandler(IExecutionRepository execRepo)
    : IQueryHandler<GetExecutionQuery, ExecutionResponse?>
{
    public async Task<ExecutionResponse?> Handle(GetExecutionQuery query, CancellationToken cancellationToken)
        => await execRepo.GetWithStepsAsync(query.ExecutionId, query.TeamAccountId, cancellationToken);
}
