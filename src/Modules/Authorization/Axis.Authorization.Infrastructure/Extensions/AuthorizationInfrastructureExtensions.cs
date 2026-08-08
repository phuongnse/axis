using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.Authorization.Infrastructure.Repositories;
using Axis.Authorization.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Authorization.Infrastructure.Extensions;

public static class AuthorizationInfrastructureExtensions
{
    public static IServiceCollection AddAuthorizationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AuthorizationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Authorization")));
        services.AddScoped<IProductRoleAssignmentStore, ProductRoleAssignmentStore>();
        services.AddScoped<IInstalledProductRoleStore, InstalledProductRoleStore>();
        services.AddScoped<IInstalledProductPolicyStore, InstalledProductPolicyStore>();
        services.AddScoped<IProductPolicyReadStore, ProductAuthorizationReadStore>();
        services.AddScoped<IAuthorizationUnitOfWork, AuthorizationUnitOfWork>();
        services.AddScoped<IAuthorizationAuditSink, AuthorizationAuditOutbox>();
        services.AddScoped<IAuthorizationAuditDispatchStore, AuthorizationAuditDispatchStore>();
        services.AddScoped<IAuthorizationAuditHealthReader, AuthorizationAuditHealthReader>();
        services.AddScoped<IProductActionDescriptorRegistry, ProductActionDescriptorRegistry>();
        services.AddScoped<IProductAuthorizationService, ProductAuthorizationService>();
        services.AddScoped<IProductPolicyInstaller, ProductPolicyInstaller>();
        services.AddScoped<ProductRoleAssignmentService>();
        services.AddScoped<ProductRoleManagementQueryService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<AuthorizationAuditDispatcher>();
        return services;
    }
}
