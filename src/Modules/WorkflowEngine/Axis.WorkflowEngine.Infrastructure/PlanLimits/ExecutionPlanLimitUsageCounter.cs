using Axis.Shared.Application.PlanLimits;
using Axis.WorkflowEngine.Application.Repositories;

namespace Axis.WorkflowEngine.Infrastructure.PlanLimits;

internal sealed class ExecutionPlanLimitUsageCounter(IExecutionRepository executionRepo) : IPlanLimitUsageCounter
{
    public PlanLimitResourceType ResourceType => PlanLimitResourceType.ExecutionsPerMonth;

    public Task<int> GetCurrentUsageAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        DateTime monthStartUtc = new DateTime(
            DateTime.UtcNow.Year,
            DateTime.UtcNow.Month,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        return executionRepo.CountCreatedSinceUtcAsync(workspaceId, monthStartUtc, cancellationToken);
    }
}
