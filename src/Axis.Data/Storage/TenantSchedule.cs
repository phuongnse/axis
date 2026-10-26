namespace Axis.Data.Storage;

/// <summary>
/// Tells the worker when a clean-up of a tenant is due: when it never ran, and then once every interval of the
/// worker's own clock. It keeps the last run of each tenant in memory, so a restarted worker runs once more.
/// </summary>
public sealed class TenantSchedule(TimeSpan interval, TimeProvider time)
{
    private readonly Dictionary<string, DateTimeOffset> _startedAt = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true, and records <paramref name="now"/> as the last run, when the tenant never ran or the interval has
    /// passed since its last run. It is recorded first, so a failure is not retried in every poll.
    /// </summary>
    public bool TryStart(string tenantId, out DateTimeOffset now)
    {
        now = time.GetUtcNow();
        if (_startedAt.TryGetValue(tenantId, out var startedAt) && now - startedAt < interval)
        {
            return false;
        }

        _startedAt[tenantId] = now;
        return true;
    }

    /// <summary>Drops the last run of a tenant that is no longer served, so it runs at once when it is served again.</summary>
    public void Forget(string tenantId) => _startedAt.Remove(tenantId);
}
