using Axis.Rules.Application;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Rules.Infrastructure.Extensions;

public static class RulesInfrastructureExtensions
{
    public static IServiceCollection AddRulesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<RulesDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Rules")));
        services.AddScoped<IRuleDefinitionRepository, RuleDefinitionRepository>();
        services.AddScoped<IUnitOfWork, RulesUnitOfWork>();
        services.AddScoped<RuleContextSchemaRegistry>();
        services.AddScoped<IRuleApplicationValidator, RuleApplicationValidator>();
        services.AddScoped<IRuleEvaluator, RuleEvaluator>();
        return services;
    }
}
