using System.Reflection;
using System.Runtime.Loader;
using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>
/// The load context of one extension package version. It shares only <c>Axis.Extensions</c> with
/// the host. Every other assembly the package needs comes from its folder, apart from the .NET
/// framework assemblies, which come from the host. No other Axis assembly of the host can be loaded
/// into it.
/// </summary>
public sealed class PackageLoadContext : AssemblyLoadContext
{
    private static readonly Assembly _contracts = typeof(IOperation).Assembly;

    private readonly AssemblyDependencyResolver _resolver;

    public PackageLoadContext(string name, string mainAssemblyPath)
        : base(name, isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        var assembly = PackageAssemblies.Resolve(_resolver, assemblyName);
        return assembly.Source switch
        {
            PackageAssemblySource.Contracts => _contracts,
            PackageAssemblySource.Package => LoadFromAssemblyPath(assembly.Path!),
            PackageAssemblySource.Refused => throw new FileNotFoundException($"Assembly '{assemblyName.Name}' is not shared with extension packages."),
            _ => null,
        };
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        ArgumentNullException.ThrowIfNull(unmanagedDllName);

        return _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path
            ? LoadUnmanagedDllFromPath(path)
            : IntPtr.Zero;
    }
}
