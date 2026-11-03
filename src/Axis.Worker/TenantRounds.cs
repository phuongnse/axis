namespace Axis.Worker;

/// <summary>
/// The active list of the worker: the tenants that have work due, in round order, each with a deficit. It runs weighted
/// deficit round-robin, see <c>docs/reference/worker-scheduling.md#fair-share</c>. When a tenant's turn starts, its deficit
/// grows by the quantum times its weight. The tenant is handed out while its deficit is at least 1, and each hand-out takes 1.
/// Then its turn ends and the next tenant has its turn. A tenant that leaves the list keeps no deficit.
/// A hand-out counts as running until <see cref="Done"/> or <see cref="Fail"/>. A tenant stays in the list while any of its
/// items runs, because a running item may queue the next one. When a claim finds nothing due while other items of the tenant run,
/// the tenant is idle: it is not handed out until one of those items ends. A claim that found a call with no lease free ends the
/// turn the same way: the tenant stays in the list, idle until one of its items ends or the next poll joins it. All members are thread-safe, because the loops
/// of the worker share one list.
/// </summary>
internal sealed class TenantRounds(int quantum, Func<string, long> weight)
{
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _list = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Busy> _busy = new(StringComparer.Ordinal);
    private int _idle;
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

    /// <summary>
    /// Adds the tenants that are not in the list yet at the end, in the given order and in one step, each with no deficit.
    /// A tenant that is in the list and idle is handed out again.
    /// </summary>
    public void Join(IEnumerable<string> tenantIds)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        lock (_gate)
        {
            foreach (var tenantId in tenantIds)
            {
                if (_index.TryGetValue(tenantId, out var node))
                {
                    Wake(node.Value);
                }
                else
                {
                    _index[tenantId] = _list.AddLast(new Entry(tenantId));
                }
            }

            UpdateSignal();
        }
    }

    /// <summary>Removes the tenant from the list, and with it its deficit. A tenant that is not in the list is ignored.</summary>
    public void Leave(string tenantId)
    {
        lock (_gate)
        {
            Remove(tenantId);
            UpdateSignal();
        }
    }

    /// <summary>
    /// Hands out the tenant whose turn it is, and takes 1 from its deficit. It returns false when no tenant in the list can be
    /// handed out. The tenant stays in the list until <see cref="Leave"/> or <see cref="Done"/>. The hand-out is running until
    /// <see cref="Done"/> or <see cref="Fail"/>, and the ticket goes to <see cref="Done"/>.
    /// </summary>
    public bool TryTake(out string tenantId, out long ticket)
    {
        lock (_gate)
        {
            while (_list.Count > _idle && _list.First is { } head)
            {
                var entry = head.Value;
                if (entry.Idle)
                {
                    _list.RemoveFirst();
                    _list.AddLast(head);
                    continue;
                }

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
                    var busy = BusyOf(tenantId);
                    busy.Running++;
                    ticket = busy.Finished;
                    UpdateSignal();
                    return true;
                }

                entry.TurnStarted = false;
                _list.RemoveFirst();
                _list.AddLast(head);
            }
        }

        tenantId = string.Empty;
        ticket = 0;
        return false;
    }

    /// <summary>
    /// Ends a hand-out whose run did not fail. When a claim found nothing due, the tenant leaves the list if none of its items
    /// runs, and is idle while another one runs, so that item may queue the next one. A tenant whose item ended after the claim
    /// began may have work that the claim missed, so it stays and is claimed again.
    /// </summary>
    public void Done(string tenantId, long ticket, bool foundWork)
    {
        lock (_gate)
        {
            var busy = BusyOf(tenantId);
            busy.Running--;
            if (foundWork)
            {
                busy.Finished++;
            }

            var running = busy.Running;
            if (running == 0)
            {
                _busy.Remove(tenantId);
            }

            if (_index.TryGetValue(tenantId, out var node))
            {
                if (foundWork)
                {
                    Wake(node.Value);
                }
                else if (busy.Finished == ticket)
                {
                    if (running == 0)
                    {
                        Remove(tenantId);
                    }
                    else if (!node.Value.Idle)
                    {
                        node.Value.Idle = true;
                        node.Value.TurnStarted = false;
                        _idle++;
                    }
                }
                else
                {
                    Wake(node.Value);
                }
            }

            UpdateSignal();
        }
    }

    /// <summary>
    /// Ends a hand-out whose claim found a call with no lease free. The tenant's turn ends and it stays in the list, idle until
    /// one of its items ends or the next poll joins it.
    /// </summary>
    public void Wait(string tenantId)
    {
        lock (_gate)
        {
            var busy = BusyOf(tenantId);
            if (--busy.Running == 0)
            {
                _busy.Remove(tenantId);
            }

            if (_index.TryGetValue(tenantId, out var node) && !node.Value.Idle)
            {
                node.Value.Idle = true;
                node.Value.TurnStarted = false;
                _idle++;
            }

            UpdateSignal();
        }
    }

    /// <summary>Ends a hand-out whose run failed. The tenant leaves the list.</summary>
    public void Fail(string tenantId)
    {
        lock (_gate)
        {
            var busy = BusyOf(tenantId);
            if (--busy.Running == 0)
            {
                _busy.Remove(tenantId);
            }

            Remove(tenantId);
            UpdateSignal();
        }
    }

    /// <summary>Completes once a tenant in the list can be handed out, at once when one can.</summary>
    public Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _list.Count > _idle ? Task.CompletedTask : _work.Task.WaitAsync(cancellationToken);
        }
    }

    private Busy BusyOf(string tenantId)
    {
        if (!_busy.TryGetValue(tenantId, out var busy))
        {
            busy = new Busy();
            _busy[tenantId] = busy;
        }

        return busy;
    }

    private void Wake(Entry entry)
    {
        if (entry.Idle)
        {
            entry.Idle = false;
            _idle--;
        }
    }

    private void Remove(string tenantId)
    {
        if (_index.Remove(tenantId, out var node))
        {
            Wake(node.Value);
            _list.Remove(node);
        }
    }

    // Under the gate. The signal is set while a tenant can be handed out, and replaced once none can.
    private void UpdateSignal()
    {
        if (_list.Count > _idle)
        {
            _work.TrySetResult();
        }
        else if (_work.Task.IsCompleted)
        {
            _work = NewSignal();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Entry(string tenantId)
    {
        public string TenantId { get; } = tenantId;

        public long Deficit { get; set; }

        public bool TurnStarted { get; set; }

        public bool Idle { get; set; }
    }

    /// <summary>The hand-outs of a tenant that run, and how many ended after finding work.</summary>
    private sealed class Busy
    {
        public int Running { get; set; }

        public long Finished { get; set; }
    }
}
