using Axis.Core.Tenancy;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers tenant resolution, the tenant connection factory and the deployment binding resolver from the
    /// <c>Tenants</c> section, the <c>configuration</c> source of the tenant catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant configuration is invalid, or outside Development a secret binding takes its value from a
    /// configuration file.
    /// </exception>
    public static IServiceCollection AddTenancy(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddTenancy(ConfigurationTenantCatalog.Load(configuration, environment));

    /// <summary>
    /// Registers tenant resolution, the tenant connection factory and the deployment binding resolver over
    /// <paramref name="catalog"/>.
    /// </summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services, ITenantCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        services.AddSingleton(catalog);
        services.AddSingleton(new TenantResolver(catalog));
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TenantConnectionFactory>();
        services.AddSingleton<ITenantConnectionFactory>(sp => sp.GetRequiredService<TenantConnectionFactory>());
        services.AddSingleton<IDeploymentBindings, DeploymentBindings>();
        return services;
    }

    /// <summary>
    /// Registers the service that gives the server or the worker the data keys and lookup keys of a tenant, read from
    /// <paramref name="source"/>.
    /// </summary>
    public static IServiceCollection AddTenantKeys(this IServiceCollection services, ITenantKeySource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);

        services.AddSingleton(source);
        services.AddSingleton<TenantKeys>();
        return services;
    }
}
