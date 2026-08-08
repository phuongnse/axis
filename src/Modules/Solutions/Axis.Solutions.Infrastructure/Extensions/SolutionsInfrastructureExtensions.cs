using Axis.Solutions.Application;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Repositories;
using Axis.Solutions.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Solutions.Infrastructure.Extensions;

public static class SolutionsInfrastructureExtensions
{
    public static IServiceCollection AddSolutionsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SolutionsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Solutions")));
        services.AddScoped<ISolutionVersionRepository, SolutionVersionRepository>();
        services.AddScoped<ISolutionInstallationRepository, SolutionInstallationRepository>();
        services.AddScoped<ISolutionOperationRepository, SolutionOperationRepository>();
        services.AddScoped<ITrustedPublisherKeyReader, TrustedPublisherKeyReader>();
        services.AddScoped<ITrustedPublisherLedger, TrustedPublisherLedger>();
        services.AddScoped<ISolutionsAuditOutbox, SolutionsAuditOutbox>();
        services.AddScoped<ISolutionsUnitOfWork, SolutionsUnitOfWork>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SolutionPackageVerifier>();
        services.AddScoped<SolutionOrchestrator>();
        services.AddScoped<PublisherReconciliationService>();
        services.AddSingleton<SolutionOperationWorker>();
        services.AddSingleton<SolutionsAuditDispatchWorker>();
        services.AddHostedService<SolutionsBackgroundService>();
        services.AddHostedService<SolutionsAuditDispatcher>();
        services.AddHostedService<TrustedPublisherConfigurationService>();
        return services;
    }
}
