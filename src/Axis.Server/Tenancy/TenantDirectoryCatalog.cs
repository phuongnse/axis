using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>
/// The tenant catalog of the server under the <c>directory</c> source. It answers from the newest directory read that
/// passed the checks, and <see cref="Replace"/> swaps in the next one. A request sees one read or the other, never a mix.
/// </summary>
internal sealed class TenantDirectoryCatalog(DirectoryTenantCatalog initial) : ITenantCatalog
{
    private volatile DirectoryTenantCatalog _current = initial;

    /// <summary>The directory read the catalog answers from now.</summary>
    public DirectoryTenantCatalog Current => _current;

    public IReadOnlyList<TenantEntry> Tenants => _current.Tenants;

    public TenantEntry? Find(string tenantId) => _current.Find(tenantId);

    public TenantEntry? FindByHost(string host) => _current.FindByHost(host);

    public bool IsUnavailableHost(string host) => _current.IsUnavailableHost(host);

    public void Replace(DirectoryTenantCatalog next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _current = next;
    }
}
