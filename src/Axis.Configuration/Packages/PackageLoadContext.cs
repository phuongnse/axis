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

        if (string.Equals(assemblyName.Name, _contracts.GetName().Name, StringComparison.Ordinal))
        {
            return _contracts;
        }

        if (_resolver.ResolveAssemblyToPath(assemblyName) is { } path)
        {
            return LoadFromAssemblyPath(path);
        }

        // Returning null would fall back to the host's context, which holds the host's Axis assemblies.
        if (assemblyName.Name?.StartsWith("Axis.", StringComparison.Ordinal) == true)
        {
            throw new FileNotFoundException($"Assembly '{assemblyName.Name}' is not shared with extension packages.");
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        ArgumentNullException.ThrowIfNull(unmanagedDllName);

        return _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path
            ? LoadUnmanagedDllFromPath(path)
            : IntPtr.Zero;
    }
}
