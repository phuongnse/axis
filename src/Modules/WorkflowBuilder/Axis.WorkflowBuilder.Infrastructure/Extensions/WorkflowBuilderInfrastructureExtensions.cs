using Axis.Shared.Application.PlanLimits;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Infrastructure.Grpc;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Infrastructure.PlanLimits;
using Axis.WorkflowBuilder.Infrastructure.Repositories;
using Axis.WorkflowBuilder.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.WorkflowBuilder.Infrastructure.Extensions;

public static class WorkflowBuilderInfrastructureExtensions
{
    public static IServiceCollection AddWorkflowBuilderInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WorkflowBuilderDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("WorkflowBuilder")
                ?? throw new InvalidOperationException("Missing connection string 'WorkflowBuilder'.")));

        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IWorkflowReferenceRepository, WorkflowReferenceRepository>();
        services.AddScoped<IWorkflowReferenceSync, WorkflowReferenceSync>();
        services.AddScoped<IUnitOfWork, WorkflowBuilderUnitOfWork>();
        services.AddScoped<IPlanLimitUsageCounter, WorkflowPlanLimitUsageCounter>();

        services.AddGrpc();

        return services;
    }

    public static WebApplication MapWorkflowBuilderGrpc(this WebApplication app)
    {
        app.MapGrpcService<WorkflowFormReferenceGrpcService>()
            .RequireAuthorization();
        return app;
    }
}
