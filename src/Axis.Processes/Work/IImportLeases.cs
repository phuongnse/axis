namespace Axis.Processes.Work;

/// <summary>
/// Takes a lease of the tenant's <c>concurrentImports</c> quota for the dry run or the commit of a record import, see
/// <c>docs/reference/tenant-quotas.md#work-at-once</c>. Only a cell's worker has one, so outside a cell no import waits.
/// </summary>
public interface IImportLeases
{
    /// <summary>
    /// Takes a lease, or returns <see langword="null"/> when the tenant has none free. Dispose the lease when the work
    /// has committed. A tenant with no <c>concurrentImports</c> limit always gets a lease that holds nothing.
    /// </summary>
    Task<IAsyncDisposable?> TryTakeAsync(string tenantId, CancellationToken cancellationToken);
}
