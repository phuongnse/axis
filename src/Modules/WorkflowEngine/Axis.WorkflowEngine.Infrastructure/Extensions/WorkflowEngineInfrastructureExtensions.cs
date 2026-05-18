using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Repositories;
using Axis.WorkflowEngine.Infrastructure.Services;
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

        return services;
    }
}
