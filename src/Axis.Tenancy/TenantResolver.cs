using System.Diagnostics.CodeAnalysis;
using Axis.Core.Tenancy;

namespace Axis.Tenancy;

/// <summary>Maps a request host to the tenant the catalog holds for it.</summary>
public sealed class TenantResolver(ITenantCatalog catalog)
{
    /// <summary>Finds the tenant for a host name without its port, ignoring letter case.</summary>
    public bool TryResolve(string? host, [NotNullWhen(true)] out TenantContext? tenant)
    {
        tenant = null;
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var entry = catalog.FindByHost(host);
        if (entry is not null)
        {
            tenant = new TenantContext(entry.Id, entry.HostSites?.GetValueOrDefault(host));
        }

        return tenant is not null;
    }

    /// <summary>
    /// Whether the host belongs to a tenant the catalog knows but does not serve, so the host answers that the tenant
    /// is not available, not that it is unknown.
    /// </summary>
    public bool IsUnavailable(string? host) => !string.IsNullOrEmpty(host) && catalog.IsUnavailableHost(host);
}
