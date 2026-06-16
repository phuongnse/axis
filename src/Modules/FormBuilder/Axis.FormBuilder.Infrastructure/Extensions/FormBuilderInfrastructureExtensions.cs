using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Infrastructure.Grpc;
using Axis.FormBuilder.Infrastructure.Tenants;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Repositories;
using Axis.FormBuilder.Infrastructure.Services;
using Axis.Shared.Application.Tenants;
using Axis.WorkflowBuilder.Contracts.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.FormBuilder.Infrastructure.Extensions;

public static class FormBuilderInfrastructureExtensions
{
    public static IServiceCollection AddFormBuilderInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FormBuilderDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("FormBuilder")
                ?? throw new InvalidOperationException("Missing connection string 'FormBuilder'.")));

        services.AddScoped<IFormRepository, FormRepository>();
        services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();
        services.AddScoped<IFormModelReferenceRepository, FormModelReferenceRepository>();
        services.AddScoped<IFormModelReferenceSync, FormModelReferenceSync>();
        services.AddScoped<IFormDeletionGuard, FormWorkflowDeletionGuard>();
        services.AddScoped<IUnitOfWork, FormBuilderUnitOfWork>();
        services.AddScoped<ITenantFormTaskCanceller, TenantFormTaskCanceller>();

        string? workflowBuilderGrpcUrl = configuration["Modules:WorkflowBuilder:GrpcUrl"];
        if (string.IsNullOrWhiteSpace(workflowBuilderGrpcUrl))
        {
            throw new InvalidOperationException(
                "Missing configuration 'Modules:WorkflowBuilder:GrpcUrl'.");
        }

        services.AddGrpcClient<WorkflowFormReferenceService.WorkflowFormReferenceServiceClient>(opts =>
        {
            opts.Address = new Uri(workflowBuilderGrpcUrl);
        });

        services.AddGrpc();

        return services;
    }

    public static WebApplication MapFormBuilderGrpc(this WebApplication app)
    {
        app.MapGrpcService<FormModelReferenceGrpcService>()
            .RequireAuthorization();
        return app;
    }
}
