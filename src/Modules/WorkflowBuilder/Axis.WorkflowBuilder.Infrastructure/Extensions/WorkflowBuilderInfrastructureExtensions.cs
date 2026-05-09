using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Infrastructure.Repositories;
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
            opts.UseNpgsql(configuration.GetConnectionString("WorkflowBuilder")));

        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IUnitOfWork, WorkflowBuilderUnitOfWork>();

        return services;
    }
}
