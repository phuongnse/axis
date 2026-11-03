namespace Axis.Processes.Work;

/// <summary>
/// Collects the tenants that got a work item due now while a scope is open, so the server or the publisher can send the wake
/// signal once the work is committed, see the wake signal in
/// <c>docs/reference/worker-scheduling.md#waking-a-tenant</c>. The scope flows with the async call, and
/// <see cref="Add"/> does nothing when no scope is open, so a worker, a command or a test scenario collects nothing.
/// </summary>
public static class WorkSignals
{
    private static readonly AsyncLocal<Scope?> _current = new();

    /// <summary>Opens a scope and makes it the current one. Disposing it restores the scope that was current before.</summary>
    public static Scope Collect()
    {
        var scope = new Scope(_current.Value);
        _current.Value = scope;
        return scope;
    }

    /// <summary>Adds <paramref name="tenantId"/> to the current scope, if there is one.</summary>
    public static void Add(string tenantId) => _current.Value?.Add(tenantId);

    /// <summary>The tenants collected while one scope was open.</summary>
    public sealed class Scope : IDisposable
    {
        private readonly Scope? _previous;
        private readonly HashSet<string> _tenants = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        internal Scope(Scope? previous) => _previous = previous;

        /// <summary>The tenants collected so far, in no defined order.</summary>
        public IReadOnlyCollection<string> Tenants
        {
            get
            {
                lock (_gate)
                {
                    return [.. _tenants];
                }
            }
        }

        /// <summary>Whether <paramref name="tenantId"/> was collected.</summary>
        public bool Contains(string tenantId)
        {
            lock (_gate)
            {
                return _tenants.Contains(tenantId);
            }
        }

        internal void Add(string tenantId)
        {
            lock (_gate)
            {
                _tenants.Add(tenantId);
            }
        }

        public void Dispose() => _current.Value = _previous;
    }
}
