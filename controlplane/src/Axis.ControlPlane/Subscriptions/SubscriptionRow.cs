namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// A row of <c>control_plane.subscriptions</c>: the subscription of one tenant, see <c>docs/reference/subscriptions.md#one-clock</c>.
/// <see cref="State"/> is <c>trialing</c>, <c>active</c>, <c>pastDue</c>, <c>readOnly</c>, <c>cancelled</c>, <c>deleting</c> or <c>deleted</c>. A tenant has at most one subscription that is
/// not <c>deleted</c>. <see cref="NextAt"/> is when the clock makes its next move, and null once the subscription is <c>deleted</c>.
/// <see cref="StepId"/> is new each time a move commits and is the id of the cell calls of the next move.
/// <see cref="PlanCode"/> and <see cref="PlanVersion"/> pin the plan version that the subscription bought, see
/// <c>docs/reference/plans.md#the-catalogue</c>. The pair is null until a plan is bought.
/// A subscription whose <see cref="PaidThrough"/> is set is a paid tenant.
/// </summary>
public sealed class SubscriptionRow
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = "";

    /// <summary>The sign-up whose confirm started the subscription.</summary>
    public Guid SignUpId { get; set; }

    public string State { get; set; } = "trialing";

    public DateTimeOffset TrialEndsAt { get; set; }

    public DateTimeOffset? NextAt { get; set; }

    public Guid StepId { get; set; }

    /// <summary>The code of the pinned plan, or null.</summary>
    public string? PlanCode { get; set; }

    /// <summary>The pinned version of the plan, or null. A catalogue that lacks a pinned version stops the start.</summary>
    public int? PlanVersion { get; set; }

    /// <summary>The date the tenant is paid through, set by the billing API, or null until then, see <c>docs/reference/billing-api.md#setting-the-paid-through-date</c>.</summary>
    public DateOnly? PaidThrough { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
