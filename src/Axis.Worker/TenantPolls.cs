namespace Axis.Worker;

/// <summary>
/// The next poll time of each tenant, on a monotonic clock that the caller reads. A tenant that has work is polled every poll
/// interval. After each poll that finds nothing due, the wait of an idle tenant doubles, up to the ceiling, and its next poll
/// comes sooner when it has an item due before the wait ends. A tenant that is not known yet is due now. See
/// <c>docs/reference/worker-scheduling.md#waking-a-tenant</c>. Only the poll of the worker uses it, so it is not thread-safe.
/// </summary>
public sealed class TenantPolls(TimeSpan pollInterval, TimeSpan ceiling)
{
    private readonly Dictionary<string, (TimeSpan NextAt, TimeSpan Wait)> _entries = new(StringComparer.Ordinal);

    /// <summary>The smallest next poll time of the known tenants, or <see langword="null"/> when none is known.</summary>
    public TimeSpan? Earliest => _entries.Count == 0 ? null : _entries.Values.Min(entry => entry.NextAt);

    /// <summary>The ids whose next poll time has come at <paramref name="now"/>, in ordinal order.</summary>
    public IReadOnlyList<string> Due(IEnumerable<string> ids, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return [.. ids.Where(id => !_entries.TryGetValue(id, out var entry) || entry.NextAt <= now).Order(StringComparer.Ordinal)];
    }

    /// <summary>A poll found work: the wait starts over, and the next poll comes after one poll interval.</summary>
    public void Found(string id, TimeSpan now) => _entries[id] = (now + pollInterval, pollInterval);

    /// <summary>
    /// A poll found nothing due. The wait doubles up to the ceiling. The next poll comes at the end of the wait, or at
    /// <paramref name="nextDue"/> from now when that is sooner. It is <see langword="null"/> when the tenant has no item.
    /// </summary>
    public void Idle(string id, TimeSpan now, TimeSpan? nextDue)
    {
        var wait = Min(Wait(id) * 2, ceiling);
        var until = Min(wait, Max(nextDue ?? wait, TimeSpan.Zero));
        _entries[id] = (now + until, wait);
    }

    /// <summary>The tenant was not ready or its read failed: it is tried again after one poll interval, and its wait stays.</summary>
    public void Retry(string id, TimeSpan now) =>
        _entries[id] = (now + pollInterval, Wait(id));

    /// <summary>
    /// A wake signal: the wait starts over, and the next poll comes within one poll interval. A sooner poll stays, and a tenant
    /// that is not known yet stays due now.
    /// </summary>
    public void Reset(string id, TimeSpan now)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            _entries[id] = (Min(entry.NextAt, now + pollInterval), pollInterval);
        }
    }

    /// <summary>Forgets every tenant, so each is due now with the first wait.</summary>
    public void ResetAll() => _entries.Clear();

    /// <summary>Forgets one tenant.</summary>
    public void Forget(string id) => _entries.Remove(id);

    private TimeSpan Wait(string id) => _entries.TryGetValue(id, out var entry) ? entry.Wait : pollInterval;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
