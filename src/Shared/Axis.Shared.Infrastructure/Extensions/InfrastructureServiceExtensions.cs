using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Shared.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers shared infrastructure services: tenant context, HTTP context accessor.
    /// Call this once in Program.cs before adding module-specific infrastructure.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        return services;
    }
}
