using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Configuration.Packages;

public static class PackageServiceCollectionExtensions
{
    /// <summary>
    /// Loads the extension packages installed under <c>Extensions:Path</c> and registers the
    /// <see cref="PackageCatalog"/>. A relative path is resolved from the content root. Without the
    /// setting the catalog is empty. Each package that was left out is logged as a warning at start.
    /// </summary>
    /// <exception cref="InvalidOperationException">The setting names a folder that does not exist.</exception>
    public static IServiceCollection AddExtensionPackages(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var path = configuration[PackageCatalog.PathSetting];
        var root = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, environment.ContentRootPath);
        services.AddSingleton(PackageCatalog.Load(root));
        services.AddHostedService<PackageDiagnosticsLogger>();
        return services;
    }
}
