using System.Reflection;
using System.Runtime.Loader;
using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>
/// Finds the assemblies for a <see cref="MetadataLoadContext"/> that reads one extension package. It
/// follows the rules of <see cref="PackageLoadContext"/>, so a check gives the same result in a host
/// that runs the package and in a host that only reads it. Nothing it finds is ever executed.
/// </summary>
internal sealed class PackageMetadataResolver : MetadataAssemblyResolver
{
    private static readonly Lazy<Dictionary<string, string>> _frameworkAssemblies = new(ListFrameworkAssemblies);

    private readonly AssemblyDependencyResolver _resolver;

    public PackageMetadataResolver(string mainAssemblyPath)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assemblyName);

        var assembly = PackageAssemblies.Resolve(_resolver, assemblyName);
        var path = assembly.Source switch
        {
            PackageAssemblySource.Contracts => typeof(IOperation).Assembly.Location,
            PackageAssemblySource.Package => assembly.Path,
            PackageAssemblySource.Host => assemblyName.Name is { } name && _frameworkAssemblies.Value.TryGetValue(name, out var found) ? found : null,
            _ => null,
        };
        return path is null ? null : context.LoadFromAssemblyPath(path);
    }

    /// <summary>The assemblies the host's default context loads, by name. The runtime lists them in <c>TRUSTED_PLATFORM_ASSEMBLIES</c>.</summary>
    private static Dictionary<string, string> ListFrameworkAssemblies()
    {
        var assemblies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
        {
            foreach (var path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                assemblies.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        return assemblies;
    }
}
