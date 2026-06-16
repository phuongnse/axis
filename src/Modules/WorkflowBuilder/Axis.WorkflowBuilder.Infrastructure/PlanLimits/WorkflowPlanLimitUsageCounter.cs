using Axis.Shared.Application.PlanLimits;
using Axis.WorkflowBuilder.Application.Repositories;

namespace Axis.WorkflowBuilder.Infrastructure.PlanLimits;

internal sealed class WorkflowPlanLimitUsageCounter(IWorkflowRepository workflowRepo) : IPlanLimitUsageCounter
{
    public PlanLimitResourceType ResourceType => PlanLimitResourceType.Workflows;

    public Task<int> GetCurrentUsageAsync(Guid teamAccountId, CancellationToken cancellationToken = default) =>
        workflowRepo.CountByTeamAccountAsync(teamAccountId, cancellationToken);
}
