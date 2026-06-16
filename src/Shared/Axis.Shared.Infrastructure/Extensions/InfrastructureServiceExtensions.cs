using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Shared.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers shared infrastructure services: workspace context, HTTP context accessor.
    /// Call this once in Program.cs before adding module-specific infrastructure.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IWorkspaceContext, HttpWorkspaceContext>();
        return services;
    }
}
