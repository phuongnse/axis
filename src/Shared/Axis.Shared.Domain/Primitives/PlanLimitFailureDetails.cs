namespace Axis.Shared.Domain.Primitives;

/// <summary>Machine-readable 402 payload for plan limit violations (F04 US-011).</summary>
public sealed record PlanLimitFailureDetails(
    string LimitType,
    int Current,
    int Max,
    string UpgradeUrl,
    string Message)
{
    public string Error => "plan_limit_exceeded";
}
