namespace Axis.ControlPlane.BillingApi;

/// <summary>
/// A row of <c>control_plane.billing_requests</c>: the answer of one billing API call that changed a subscription, kept so that a retry with the same
/// <see cref="IdempotencyKey"/> and the same <see cref="RequestHash"/> gets the same <see cref="Answer"/> for 24 hours, see
/// <c>docs/reference/billing-api.md#idempotency</c>. The key is not unique, as a key may be used again after the window.
/// </summary>
public sealed class BillingRequestRow
{
    public Guid Id { get; set; }

    public string IdempotencyKey { get; set; } = "";

    /// <summary>The SHA-256 of the route, the tenant and the body, as lowercase hex.</summary>
    public string RequestHash { get; set; } = "";

    /// <summary>The JSON text that the first call answered.</summary>
    public string Answer { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
