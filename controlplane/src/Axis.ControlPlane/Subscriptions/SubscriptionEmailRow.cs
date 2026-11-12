namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// A row of <c>control_plane.subscription_emails</c>: a timeline email that the clock queued for a subscription, see
/// <c>docs/reference/subscriptions.md#emails</c>. The key is the subscription and the <see cref="Kind"/>, the name of the email's
/// template, so each kind is queued once per subscription.
/// </summary>
public sealed class SubscriptionEmailRow
{
    public Guid SubscriptionId { get; set; }

    public string Kind { get; set; } = "";

    public DateTimeOffset QueuedAt { get; set; }
}
