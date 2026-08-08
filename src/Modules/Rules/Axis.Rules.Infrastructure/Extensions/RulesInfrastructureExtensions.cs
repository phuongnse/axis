using Axis.Rules.Application;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Search;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Repositories;
using Axis.Rules.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Rules.Infrastructure.Extensions;

public static class RulesInfrastructureExtensions
{
    public static IServiceCollection AddRulesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<RulesDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Rules")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRuleDefinitionRepository, RuleDefinitionRepository>();
        services.AddScoped<IRuleBindingRepository, RuleBindingRepository>();
        services.AddScoped<IRuleCatalogSearchProvider, PostgresRuleCatalogSearchProvider>();
        services.AddScoped<IRuleTextSearchProvider, PostgresRuleTextSearchProvider>();
        services.AddScoped<IUnitOfWork, RulesUnitOfWork>();
        services.AddScoped<RuleConditionProjectionService>();
        services.AddScoped<RuleAuthoringLanguageService>();
        services.AddScoped<RuleExpressionGuideService>();
        services.AddScoped<IRuleApplicationValidator, RuleApplicationValidator>();
        services.AddScoped<IRuleEvaluator, RuleEvaluator>();
        services.AddScoped<IRuleBindingEvaluator, RuleBindingEvaluator>();
        services.AddScoped<IRuleBindingReferenceValidator, RuleBindingReferenceValidator>();
        services.AddScoped<IRuleBindingSolutionInstaller, RuleBindingSolutionInstaller>();
        return services;
    }
}
