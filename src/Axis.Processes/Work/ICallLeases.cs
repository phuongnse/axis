namespace Axis.Processes.Work;

/// <summary>
/// Takes a lease of the tenant's <c>concurrentConnectorCalls</c> quota for a <c>connector.call</c> or <c>package.call</c> work
/// item, see <c>docs/reference/tenant-quotas.md#work-at-once</c>. The runner claims such an item only with a lease and holds
/// it until the run ends. Only a cell's worker has one, so outside a cell no call waits.
/// </summary>
public interface ICallLeases
{
    /// <summary>
    /// Takes a lease, or returns <see langword="null"/> when the tenant has none free. Dispose the lease when the run
    /// has ended. A tenant with no <c>concurrentConnectorCalls</c> limit always gets a lease that holds nothing.
    /// </summary>
    Task<IAsyncDisposable?> TryTakeAsync(string tenantId, CancellationToken cancellationToken);
}
