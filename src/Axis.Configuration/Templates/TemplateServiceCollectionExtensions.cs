using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Configuration.Templates;

public static class TemplateServiceCollectionExtensions
{
    /// <summary>
    /// Loads the templates installed under <c>Templates:Path</c> and registers the
    /// <see cref="TemplateCatalog"/>. A relative path is resolved from the content root. Without the
    /// setting the catalog is empty. Each template version that was left out is logged as a warning at start.
    /// </summary>
    /// <exception cref="InvalidOperationException">The setting names a folder that does not exist.</exception>
    public static IServiceCollection AddTemplates(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var path = configuration[TemplateCatalog.PathSetting];
        var root = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, environment.ContentRootPath);
        services.AddSingleton(TemplateCatalog.Load(root));
        services.AddHostedService<TemplateDiagnosticsLogger>();
        return services;
    }
}
