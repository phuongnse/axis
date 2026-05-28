using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Infrastructure.Grpc;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.DataModeling.Infrastructure.Repositories;
using Axis.FormBuilder.Contracts.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.DataModeling.Infrastructure.Extensions;

public static class DataModelingInfrastructureExtensions
{
    public static IServiceCollection AddDataModelingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DataModelingDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("DataModeling")));

        services.AddScoped<IDataModelRepository, DataModelRepository>();
        services.AddScoped<IDataClassRepository, DataClassRepository>();
        services.AddScoped<IDataRecordRepository, DataRecordRepository>();
        services.AddScoped<IUnitOfWork, DataModelingUnitOfWork>();
        services.AddScoped<IModelDeletionGuard, FormModelDeletionGuard>();

        string? formBuilderGrpcUrl = configuration["Modules:FormBuilder:GrpcUrl"];
        if (string.IsNullOrWhiteSpace(formBuilderGrpcUrl))
        {
            throw new InvalidOperationException(
                "Missing configuration 'Modules:FormBuilder:GrpcUrl'.");
        }

        services.AddGrpcClient<FormModelReferenceService.FormModelReferenceServiceClient>(opts =>
        {
            opts.Address = new Uri(formBuilderGrpcUrl);
        });

        services.AddGrpc();

        return services;
    }

    public static WebApplication MapDataModelingGrpc(this WebApplication app)
    {
        app.MapGrpcService<DataModelCatalogGrpcService>()
            .RequireAuthorization();
        return app;
    }
}
