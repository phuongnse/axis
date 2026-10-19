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
            tenant = new TenantContext(entry.Id);
        }

        return tenant is not null;
    }
}
