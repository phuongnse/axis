namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// A row of <c>control_plane.quota_overrides</c>: the value that an operator set for one quota of a subscription, see
/// <c>docs/reference/plans.md#from-a-plan-to-the-quotas</c>. The key is the subscription and the <see cref="Quota"/>, so a new override
/// replaces the old one. The control plane writes <see cref="Value"/> to the cell instead of the plan's value until <see cref="Until"/>,
/// and it names the <see cref="Operator"/> and the <see cref="Reason"/>.
/// </summary>
public sealed class QuotaOverrideRow
{
    public Guid SubscriptionId { get; set; }

    public string Quota { get; set; } = "";

    public long Value { get; set; }

    public string Reason { get; set; } = "";

    public DateTimeOffset Until { get; set; }

    public string Operator { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
