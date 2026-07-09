using Axis.Rules.Application;
using Axis.Rules.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Rules.Infrastructure.Extensions;

public static class RulesInfrastructureExtensions
{
    public static IServiceCollection AddRulesInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFieldRuleDefinitionProvider, SystemFieldRuleDefinitionProvider>();
        services.AddSingleton<IFieldRuleApplicationValidator, FieldRuleApplicationValidator>();
        return services;
    }
}
