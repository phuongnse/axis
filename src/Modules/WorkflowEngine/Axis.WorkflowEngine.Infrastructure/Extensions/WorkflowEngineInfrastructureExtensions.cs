using Axis.Shared.Application.Organizations;
using Axis.Shared.Application.PlanLimits;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Infrastructure.Organizations;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.PlanLimits;
using Axis.WorkflowEngine.Infrastructure.Repositories;
using Axis.WorkflowEngine.Infrastructure.Services;
using Axis.WorkflowEngine.Infrastructure.Services.StepExecutors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.WorkflowEngine.Infrastructure.Extensions;

public static class WorkflowEngineInfrastructureExtensions
{
    public static IServiceCollection AddWorkflowEngineInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WorkflowEngineDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("WorkflowEngine")
                ?? throw new InvalidOperationException("Missing connection string 'WorkflowEngine'.")));

        services.AddScoped<IExecutionRepository, ExecutionRepository>();
        services.AddScoped<IWorkflowDefinitionReader, WorkflowDefinitionReader>();
        services.AddScoped<IUnitOfWork, WorkflowEngineUnitOfWork>();
        services.AddScoped<IPlanLimitUsageCounter, ExecutionPlanLimitUsageCounter>();
        services.AddScoped<IOrganizationExecutionCanceller, OrganizationExecutionCanceller>();

        // Step executor services (Infrastructure implements Application interfaces)
        services.AddScoped<IHttpStepExecutor, HttpStepExecutor>();
        services.AddScoped<IScriptExecutor, ScriptExecutor>();
        services.AddScoped<INotificationSender, NotificationSender>();

        // Wolverine message dispatcher abstraction
        services.AddScoped<IStepDispatcher, WolverineStepDispatcher>();

        // HttpClient for HTTP steps
        services.AddHttpClient("StepExecutor");

        return services;
    }
}
