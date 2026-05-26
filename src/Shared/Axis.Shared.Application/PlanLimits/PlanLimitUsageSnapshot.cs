namespace Axis.Shared.Application.PlanLimits;

public sealed record PlanLimitUsageSnapshot(
    int WorkflowsUsed,
    int? WorkflowsLimit,
    int ExecutionsUsedThisMonth,
    int? ExecutionsPerMonthLimit,
    int UsersUsed,
    int? UsersLimit);
