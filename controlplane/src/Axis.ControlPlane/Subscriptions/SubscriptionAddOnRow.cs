namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// A row of <c>control_plane.subscription_add_ons</c>: the packs of one add-on that a subscription bought, see
/// <c>docs/reference/plans.md#from-a-plan-to-the-quotas</c>. The key is the subscription and the <see cref="AddOnCode"/>, a code that the
/// pinned plan version lists, and <see cref="Quantity"/> is how many packs of it the subscription holds.
/// </summary>
public sealed class SubscriptionAddOnRow
{
    public Guid SubscriptionId { get; set; }

    public string AddOnCode { get; set; } = "";

    public int Quantity { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
