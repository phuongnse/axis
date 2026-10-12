using Axis.Core.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers tenant resolution, the tenant connection factory and the deployment binding resolver from the
    /// <c>Tenants</c> section.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant configuration is invalid.</exception>
    public static IServiceCollection AddTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new TenantOptions();
        configuration.GetSection(TenantOptions.SectionName).Bind(options.Tenants);

        var problems = TenantOptionsValidator.Validate(options);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The tenant configuration is invalid. {string.Join(" ", problems)}");
        }

        services.AddSingleton(options);
        services.AddSingleton(new TenantResolver(options));
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<ITenantConnectionFactory, TenantConnectionFactory>();
        services.AddSingleton<IDeploymentBindings, DeploymentBindings>();
        return services;
    }
}
