namespace Axis.ControlPlane.Billing;

/// <summary>
/// A row of <c>billing.webhook_inbox</c>: one signed event of a payment provider, kept once, see <c>docs/reference/payments.md#webhooks</c>. The row is unique on
/// <see cref="Provider"/> and <see cref="EventId"/>, so a repeated delivery changes nothing. <see cref="OccurredAt"/> is the time the provider gives the event, and
/// <see cref="Outcome"/> stays empty until the job has applied the event or has marked it out of place.
/// </summary>
public sealed class WebhookInboxRow
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = "";

    public string EventId { get; set; } = "";

    public long? OrderCode { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }

    public string Body { get; set; } = "";

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary><c>applied</c> or <c>outOfPlace</c>, and empty while the event waits.</summary>
    public string? Outcome { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
