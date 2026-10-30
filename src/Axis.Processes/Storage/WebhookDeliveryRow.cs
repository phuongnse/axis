namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.webhook_deliveries</c>: the delivery of one event occurrence to one webhook, which a
/// <c>raiseEvent</c> step staged. It queues one <c>webhook.deliver</c> work item for the row in the same transaction.
/// The row holds no body. Each attempt builds the body again from the stored occurrence. Times use the database clock.
/// </summary>
public sealed class WebhookDeliveryRow
{
    /// <summary>The row's id, a version 7 UUID. The row's work item names it as its <c>webhook_delivery_id</c>.</summary>
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The release the raising instance is pinned to. It compiled the step and holds the webhook.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The webhook of the release that the event is sent to.</summary>
    public required Guid WebhookId { get; init; }

    /// <summary>The event occurrence in <c>axis.events</c>. Together with the webhook it is unique.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The state of the delivery: <c>pending</c> or <c>delivered</c>.</summary>
    public string State { get; init; } = "pending";

    /// <summary>How many requests were sent or tried so far.</summary>
    public int Attempts { get; init; }

    /// <summary>The HTTP status of the last attempt, or null before one and after an attempt that got no answer.</summary>
    public int? StatusCode { get; init; }

    /// <summary>The start of the staging step's transaction.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the receiver answered <c>2xx</c>, or null while the delivery is pending.</summary>
    public DateTimeOffset? DeliveredAt { get; init; }
}
