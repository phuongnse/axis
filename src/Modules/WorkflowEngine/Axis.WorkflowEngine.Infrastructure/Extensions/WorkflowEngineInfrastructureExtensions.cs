using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Repositories;
using Axis.WorkflowEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.WorkflowEngine.Infrastructure.Extensions;

public static class WorkflowEngineInfrastructureExtensions
{
    public static IServiceCollection AddWorkflowEngineInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WorkflowEngineDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddScoped<IExecutionRepository, ExecutionRepository>();
        services.AddScoped<IWorkflowDefinitionReader, WorkflowDefinitionReader>();
        services.AddScoped<IUnitOfWork, WorkflowEngineUnitOfWork>();

        return services;
    }
}
