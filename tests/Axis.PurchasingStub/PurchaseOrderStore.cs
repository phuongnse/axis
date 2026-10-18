namespace Axis.PurchasingStub;

internal sealed record PurchaseOrder(
    string OrderNumber,
    string IdempotencyKey,
    string RequestNumber,
    string? SupplierCode,
    decimal Amount,
    DateTimeOffset CreatedAt);

/// <summary>
/// The stub's orders, by idempotency key and in creation order, and the drops and holds that tests
/// ask for. Each drop and each hold happens once. A request made unavailable stays so until a test
/// makes it available again. A request made unknown has one create that misfits, and lookups that
/// answer <c>503</c> for good.
/// </summary>
internal sealed class PurchaseOrderStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, PurchaseOrder> _byKey = new(StringComparer.Ordinal);
    private readonly List<PurchaseOrder> _orders = [];
    private readonly HashSet<string> _dropKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dropRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailableRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lookupUnavailableRequests = new(StringComparer.Ordinal);

    // The holds no create has met yet, and the holds not yet released, by request number.
    private readonly Dictionary<string, TaskCompletionSource> _holds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource> _releases = new(StringComparer.Ordinal);
    private bool _dropNext;
    private int _count;

    /// <summary>
    /// Returns the order for the key, creating it when the key is new, whether this create should
    /// lose its response, whether its answer should misfit the declared output, and the task to wait
    /// for before answering when the create is held, or <see langword="null"/>. A known key returns
    /// the stored order, even when the body differs.
    /// </summary>
    public (PurchaseOrder Order, bool Created, bool Drop, bool Misfit, Task? Hold) Create(string key, string requestNumber, string? supplierCode, decimal amount)
    {
        lock (_lock)
        {
            var droppedByKey = _dropKeys.Remove(key);
            var droppedByRequest = _dropRequests.Remove(requestNumber);
            var drop = _dropNext || droppedByKey || droppedByRequest;
            _dropNext = false;
            var misfit = _unknownRequests.Remove(requestNumber);
            var hold = _holds.Remove(requestNumber, out var held) ? held.Task : null;

            if (_byKey.TryGetValue(key, out var existing))
            {
                return (existing, false, drop, misfit, hold);
            }

            var now = DateTimeOffset.UtcNow;
            _count++;
            var order = new PurchaseOrder($"PO-{now.Year}-{_count:D4}", key, requestNumber, supplierCode, amount, now);
            _byKey.Add(key, order);
            _orders.Add(order);
            return (order, true, drop, misfit, hold);
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

    public void DropRequest(string requestNumber)
    {
        lock (_lock)
        {
            _dropRequests.Add(requestNumber);
        }
    }

    /// <summary>Makes every create for the request fail with <c>503</c> until <see cref="MakeAvailable"/>.</summary>
    public void MakeUnavailable(string requestNumber)
    {
        lock (_lock)
        {
            _unavailableRequests.Add(requestNumber);
        }
    }

    /// <summary>Lets creates for the request succeed again.</summary>
    public void MakeAvailable(string requestNumber)
    {
        lock (_lock)
        {
            _unavailableRequests.Remove(requestNumber);
        }
    }

    public bool IsUnavailable(string requestNumber)
    {
        lock (_lock)
        {
            return _unavailableRequests.Contains(requestNumber);
        }
    }

    /// <summary>
    /// Makes the purchase order call for the request end <c>unknown</c> and stay so. The first create
    /// stores the order but answers without its number, and every lookup for the request answers
    /// <c>503</c>. A dropped response would not do, because the connector's idempotency sends it again.
    /// </summary>
    public void MakeUnknown(string requestNumber)
    {
        lock (_lock)
        {
            _unknownRequests.Add(requestNumber);
            _lookupUnavailableRequests.Add(requestNumber);
        }
    }

    /// <summary>Whether lookups of the order answer <c>503</c>, because its request was made unknown.</summary>
    public bool IsLookupUnavailable(PurchaseOrder order)
    {
        lock (_lock)
        {
            return _lookupUnavailableRequests.Contains(order.RequestNumber);
        }
    }

    /// <summary>Holds back the response of the next create for the request until <see cref="Release"/>.</summary>
    public void HoldRequest(string requestNumber)
    {
        lock (_lock)
        {
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holds[requestNumber] = hold;
            _releases[requestNumber] = hold;
        }
    }

    /// <summary>Lets the held create for the request answer. Does nothing when no hold is waiting.</summary>
    public void Release(string requestNumber)
    {
        lock (_lock)
        {
            _holds.Remove(requestNumber);
            if (_releases.Remove(requestNumber, out var hold))
            {
                hold.TrySetResult();
            }
        }
    }
}
