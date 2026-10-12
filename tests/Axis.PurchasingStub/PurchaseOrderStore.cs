namespace Axis.PurchasingStub;

internal sealed record PurchaseOrder(
    string OrderNumber,
    string IdempotencyKey,
    string RequestNumber,
    string? SupplierCode,
    decimal Amount,
    DateTimeOffset CreatedAt);

/// <summary>
/// The stub's orders, by idempotency key and in creation order, and the drops that tests ask for.
/// Each drop happens once.
/// </summary>
internal sealed class PurchaseOrderStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, PurchaseOrder> _byKey = new(StringComparer.Ordinal);
    private readonly List<PurchaseOrder> _orders = [];
    private readonly HashSet<string> _dropKeys = new(StringComparer.Ordinal);
    private bool _dropNext;
    private int _count;

    /// <summary>
    /// Returns the order for the key, creating it when the key is new, and whether this create
    /// should lose its response. A known key returns the stored order, even when the body differs.
    /// </summary>
    public (PurchaseOrder Order, bool Created, bool Drop) Create(string key, string requestNumber, string? supplierCode, decimal amount)
    {
        lock (_lock)
        {
            var droppedByKey = _dropKeys.Remove(key);
            var drop = _dropNext || droppedByKey;
            _dropNext = false;

            if (_byKey.TryGetValue(key, out var existing))
            {
                return (existing, false, drop);
            }

            var now = DateTimeOffset.UtcNow;
            _count++;
            var order = new PurchaseOrder($"PO-{now.Year}-{_count:D4}", key, requestNumber, supplierCode, amount, now);
            _byKey.Add(key, order);
            _orders.Add(order);
            return (order, true, drop);
        }
    }

    public PurchaseOrder? Find(string key)
    {
        lock (_lock)
        {
            return _byKey.GetValueOrDefault(key);
        }
    }

    public IReadOnlyList<PurchaseOrder> List()
    {
        lock (_lock)
        {
            return [.. _orders];
        }
    }

    public void DropNext()
    {
        lock (_lock)
        {
            _dropNext = true;
        }
    }

    public void DropKey(string key)
    {
        lock (_lock)
        {
            _dropKeys.Add(key);
        }
    }
}
