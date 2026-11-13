using System.Reflection;
using System.Runtime.Loader;
using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>Where an assembly that a package refers to comes from.</summary>
internal enum PackageAssemblySource
{
    /// <summary>The host's <c>Axis.Extensions</c>, which every package shares.</summary>
    Contracts,

    /// <summary>A file in the package folder, at <see cref="PackageAssembly.Path"/>.</summary>
    Package,

    /// <summary>Another Axis assembly of the host, which no package can use.</summary>
    Refused,

    /// <summary>The host, which supplies the .NET framework assemblies.</summary>
    Host,
}

internal readonly record struct PackageAssembly(PackageAssemblySource Source, string? Path = null);

/// <summary>
/// The one rule that finds the assemblies of an extension package, for the load context that runs a
/// package and for the metadata-only context that only reads it, see
/// <c>docs/reference/extension-packages.md#loading</c>.
/// </summary>
internal static class PackageAssemblies
{
    private static readonly string _contractsName = typeof(IOperation).Assembly.GetName().Name!;

    public static PackageAssembly Resolve(AssemblyDependencyResolver resolver, AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, _contractsName, StringComparison.Ordinal))
        {
            return new PackageAssembly(PackageAssemblySource.Contracts);
        }

        if (resolver.ResolveAssemblyToPath(assemblyName) is { } path)
        {
            return new PackageAssembly(PackageAssemblySource.Package, path);
        }

        // Falling back to the host would give the package the host's Axis assemblies.
        return assemblyName.Name?.StartsWith("Axis.", StringComparison.Ordinal) == true
            ? new PackageAssembly(PackageAssemblySource.Refused)
            : new PackageAssembly(PackageAssemblySource.Host);
    }
}
