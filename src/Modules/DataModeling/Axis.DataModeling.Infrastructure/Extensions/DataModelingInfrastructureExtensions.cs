using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.DataModeling.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.DataModeling.Infrastructure.Extensions;

public static class DataModelingInfrastructureExtensions
{
    public static IServiceCollection AddDataModelingInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DataModelingDbContext>((sp, options) =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IDataModelRepository, DataModelRepository>();
        services.AddScoped<IDataClassRepository, DataClassRepository>();
        services.AddScoped<IDataRecordRepository, DataRecordRepository>();
        services.AddScoped<IUnitOfWork, DataModelingUnitOfWork>();

        return services;
    }
}
