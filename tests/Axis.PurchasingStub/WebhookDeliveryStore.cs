namespace Axis.PurchasingStub;

internal sealed record WebhookDelivery(string WebhookId, string? Type, string RequestNumber, DateTimeOffset ReceivedAt);

/// <summary>
/// The webhook deliveries that the stub accepted, each stored once by its <c>webhook-id</c> and listed in arrival
/// order, and the request numbers whose deliveries a test has made fail. Deliveries live in memory only.
/// </summary>
internal sealed class WebhookDeliveryStore
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _webhookIds = new(StringComparer.Ordinal);
    private readonly List<WebhookDelivery> _deliveries = [];
    private readonly HashSet<string> _failingRequests = new(StringComparer.Ordinal);

    /// <summary>Stores the delivery, unless its webhook id is already stored. Returns whether it was stored.</summary>
    public bool Add(WebhookDelivery delivery)
    {
        lock (_lock)
        {
            if (!_webhookIds.Add(delivery.WebhookId))
            {
                return false;
            }

            _deliveries.Add(delivery);
            return true;
        }
    }

    public IReadOnlyList<WebhookDelivery> List()
    {
        lock (_lock)
        {
            return [.. _deliveries];
        }
    }

    public void Fail(string requestNumber)
    {
        lock (_lock)
        {
            _failingRequests.Add(requestNumber);
        }
    }

    public void Accept(string requestNumber)
    {
        lock (_lock)
        {
            _failingRequests.Remove(requestNumber);
        }
    }

    public bool IsFailing(string requestNumber)
    {
        lock (_lock)
        {
            return _failingRequests.Contains(requestNumber);
        }
    }
}
