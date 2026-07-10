using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.BusinessObjects.Infrastructure.Repositories;
using Axis.Rules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.BusinessObjects.Infrastructure.Extensions;

public static class BusinessObjectsInfrastructureExtensions
{
    public static IServiceCollection AddBusinessObjectsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<BusinessObjectsDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("BusinessObjects")));

        services.AddScoped<IBusinessObjectDefinitionRepository, BusinessObjectDefinitionRepository>();
        services.AddScoped<IBusinessObjectDefinitionInputPlanner, BusinessObjectDefinitionInputPlanner>();
        services.AddScoped<IUnitOfWork, BusinessObjectsUnitOfWork>();
        services.AddSingleton<IRuleContextSchemaProvider, BusinessObjectRuleContextSchemaProvider>();

        return services;
    }
}
