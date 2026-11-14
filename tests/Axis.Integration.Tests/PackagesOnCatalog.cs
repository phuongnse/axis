using Axis.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant catalog that holds the tenant's application packages switch on, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. The configuration source has no switch, so a test host that
/// exercises a package file write turns it on for every tenant through <see cref="Switch"/>.
/// </summary>
internal sealed class PackagesOnCatalog(ITenantCatalog inner) : ITenantCatalog
{
    public IReadOnlyList<TenantEntry> Tenants => inner.Tenants;

    /// <summary>Replaces the host's registered catalog with one that has the switch on for every tenant.</summary>
    public static void Switch(IServiceCollection services)
    {
        var registered = services.Last(descriptor => descriptor.ServiceType == typeof(ITenantCatalog));
        if (registered.ImplementationInstance is ITenantCatalog instance)
        {
            services.AddSingleton<ITenantCatalog>(new PackagesOnCatalog(instance));
        }
        else
        {
            var factory = registered.ImplementationFactory ?? throw new InvalidOperationException("The tenant catalog has no instance or factory.");
            services.AddSingleton<ITenantCatalog>(provider => new PackagesOnCatalog((ITenantCatalog)factory(provider)));
        }
    }

    public TenantEntry? Find(string tenantId) => inner.Find(tenantId) is { } entry ? entry with { ApplicationPackages = true } : null;

    public TenantEntry? FindByHost(string host) => inner.FindByHost(host) is { } entry ? entry with { ApplicationPackages = true } : null;

    public bool IsUnavailableHost(string host) => inner.IsUnavailableHost(host);
}
