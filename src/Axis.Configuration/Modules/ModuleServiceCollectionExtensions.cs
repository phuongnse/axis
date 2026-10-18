using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Configuration.Modules;

public static class ModuleServiceCollectionExtensions
{
    /// <summary>
    /// Loads the modules installed under <c>Modules:Path</c> and registers the
    /// <see cref="ModuleCatalog"/>. A relative path is resolved from the content root. Without the
    /// setting the catalog is empty. Each module version that was left out is logged as a warning at start.
    /// </summary>
    /// <exception cref="InvalidOperationException">The setting names a folder that does not exist.</exception>
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var path = configuration[ModuleCatalog.PathSetting];
        var root = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, environment.ContentRootPath);
        services.AddSingleton(ModuleCatalog.Load(root));
        services.AddHostedService<ModuleDiagnosticsLogger>();
        return services;
    }
}
