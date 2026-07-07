using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Infrastructure.Persistence;
using Axis.Objects.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Objects.Infrastructure.Extensions;

public static class ObjectsInfrastructureExtensions
{
    public static IServiceCollection AddObjectsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ObjectsDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("Objects")));

        services.AddScoped<IObjectDefinitionRepository, ObjectDefinitionRepository>();
        services.AddScoped<IUnitOfWork, ObjectsUnitOfWork>();

        return services;
    }
}
