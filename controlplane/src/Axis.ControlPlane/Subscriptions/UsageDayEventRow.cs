namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// A row of <c>control_plane.usage_day_events</c>: a <c>usage.daily</c> event that the control plane queued for a subscription and a UTC day, see
/// <c>docs/reference/billing-api.md#daily-usage</c>. The key is the subscription and the <see cref="Day"/>, so each day is queued once per subscription.
/// </summary>
public sealed class UsageDayEventRow
{
    public Guid SubscriptionId { get; set; }

    public DateOnly Day { get; set; }

    public DateTimeOffset QueuedAt { get; set; }
}
