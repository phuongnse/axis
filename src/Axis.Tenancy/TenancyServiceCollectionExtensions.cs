using Axis.Core.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers tenant resolution, the tenant connection factory and the deployment binding resolver from the
    /// <c>Tenants</c> section.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant configuration is invalid, or outside Development a secret binding takes its value from a
    /// configuration file.
    /// </exception>
    public static IServiceCollection AddTenancy(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new TenantOptions();
        configuration.GetSection(TenantOptions.SectionName).Bind(options.Tenants);

        var problems = TenantOptionsValidator.Validate(options).ToList();
        if (!environment.IsDevelopment())
        {
            problems.AddRange(SecretBindingSources.Validate(configuration, options));
        }

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
