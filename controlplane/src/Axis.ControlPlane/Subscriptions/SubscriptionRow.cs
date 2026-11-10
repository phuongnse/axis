namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// A row of <c>control_plane.subscriptions</c>: the subscription of one tenant, see <c>docs/reference/subscriptions.md#one-clock</c>.
/// <see cref="State"/> is <c>trialing</c>, <c>readOnly</c>, <c>deleting</c> or <c>deleted</c>. A tenant has at most one subscription that is
/// not <c>deleted</c>. <see cref="NextAt"/> is when the clock makes its next move, and null once the subscription is <c>deleted</c>.
/// <see cref="StepId"/> is new each time a move commits and is the id of the cell calls of the next move.
/// <see cref="PlanCode"/> and <see cref="PlanVersion"/> pin the plan version that the subscription bought, and <see cref="Currency"/> and
/// <see cref="Period"/> its currency and period, see <c>docs/reference/plans.md#the-catalogue</c>. Each pair is null until a plan is bought.
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

    /// <summary><c>VND</c> or <c>USD</c>, or null. Set only with a plan.</summary>
    public string? Currency { get; set; }

    /// <summary><c>month</c> or <c>year</c>, or null.</summary>
    public string? Period { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
