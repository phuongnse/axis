namespace Axis.Worker;

/// <summary>
/// The active list of the worker: the tenants that have work due, in round order, each with a deficit. It runs weighted
/// deficit round-robin, see <c>docs/reference/worker-scheduling.md#fair-share</c>. When a tenant's turn starts, its deficit
/// grows by the quantum times its weight. The tenant is handed out while its deficit is at least 1, and each hand-out takes 1.
/// Then its turn ends and the next tenant has its turn. A tenant that leaves the list keeps no deficit. All members are
/// thread-safe, because the loops of the worker share one list.
/// </summary>
internal sealed class TenantRounds(int quantum, Func<string, long> weight)
{
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _list = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);
    private TaskCompletionSource _work = NewSignal();

    /// <summary>The tenants in the list.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _list.Count;
            }
        }
    }

    /// <summary>Adds the tenants that are not in the list yet at the end, in the given order and in one step, each with no deficit.</summary>
    public void Join(IEnumerable<string> tenantIds)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        lock (_gate)
        {
            foreach (var tenantId in tenantIds)
            {
                if (!_index.ContainsKey(tenantId))
                {
                    _index[tenantId] = _list.AddLast(new Entry(tenantId));
                }
            }

            if (_list.Count > 0)
            {
                _work.TrySetResult();
            }
        }
    }

    /// <summary>Removes the tenant from the list, and with it its deficit. A tenant that is not in the list is ignored.</summary>
    public void Leave(string tenantId)
    {
        lock (_gate)
        {
            if (!_index.Remove(tenantId, out var node))
            {
                return;
            }

            _list.Remove(node);
            if (_list.Count == 0 && _work.Task.IsCompleted)
            {
                _work = NewSignal();
            }
        }
    }

    /// <summary>
    /// Hands out the tenant whose turn it is, and takes 1 from its deficit. It returns false when the list is empty.
    /// The tenant stays in the list until <see cref="Leave"/>.
    /// </summary>
    public bool TryTake(out string tenantId)
    {
        lock (_gate)
        {
            while (_list.First is { } head)
            {
                var entry = head.Value;
                if (!entry.TurnStarted)
                {
                    var share = quantum * Math.Min(Math.Max(1, weight(entry.TenantId)), int.MaxValue);
                    entry.Deficit = Math.Min(entry.Deficit + share, int.MaxValue);
                    entry.TurnStarted = true;
                }

                if (entry.Deficit >= 1)
                {
                    entry.Deficit--;
                    tenantId = entry.TenantId;
                    return true;
                }

                entry.TurnStarted = false;
                _list.RemoveFirst();
                _list.AddLast(head);
            }
        }

        tenantId = string.Empty;
        return false;
    }

    /// <summary>Completes once the list holds a tenant, at once when it already does.</summary>
    public Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _list.Count > 0 ? Task.CompletedTask : _work.Task.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Entry(string tenantId)
    {
        public string TenantId { get; } = tenantId;

        public long Deficit { get; set; }

        public bool TurnStarted { get; set; }
    }
}
