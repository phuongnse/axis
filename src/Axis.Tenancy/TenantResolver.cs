using System.Diagnostics.CodeAnalysis;
using Axis.Core.Tenancy;

namespace Axis.Tenancy;

/// <summary>Maps a request host to the tenant configured for it.</summary>
public sealed class TenantResolver
{
    private readonly Dictionary<string, TenantContext> _tenantsByHost = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="options">Tenant configuration that passed <see cref="TenantOptionsValidator"/>.</param>
    public TenantResolver(TenantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var (tenantId, settings) in options.Tenants)
        {
            var tenant = new TenantContext(tenantId);
            foreach (var host in settings.Hosts)
            {
                _tenantsByHost[host] = tenant;
            }
        }
    }

    /// <summary>Finds the tenant for a host name without its port, ignoring letter case.</summary>
    public bool TryResolve(string? host, [NotNullWhen(true)] out TenantContext? tenant)
    {
        tenant = null;
        return !string.IsNullOrEmpty(host) && _tenantsByHost.TryGetValue(host, out tenant);
    }
}
