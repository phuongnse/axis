using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>An installed extension package version whose manifest matches its assembly, loaded in its own context.</summary>
public sealed class InstalledPackage
{
    private readonly Dictionary<string, Type> _operationTypes;

    internal InstalledPackage(PackageManifest manifest, PackageLoadContext? loadContext, Dictionary<string, Type> operationTypes)
    {
        Manifest = manifest;
        LoadContext = loadContext;
        _operationTypes = operationTypes;
    }

    public PackageManifest Manifest { get; }

    /// <summary>The context the assembly is loaded in, or null for a <c>node</c> or <c>python</c> package, which has no assembly.</summary>
    public PackageLoadContext? LoadContext { get; }

    /// <summary>Creates a new instance of the operation named <paramref name="operationName"/>, ignoring letter case.</summary>
    /// <exception cref="ArgumentException">The package has no such operation.</exception>
    /// <exception cref="InvalidOperationException">The package is a <c>node</c> or <c>python</c> package.</exception>
    public IOperation CreateOperation(string operationName)
    {
        if (LoadContext is null)
        {
            throw new InvalidOperationException(
                $"Package '{Manifest.Id}' {Manifest.Version} is a '{Manifest.Language.ToString().ToLowerInvariant()}' package, whose code runs only in a package runner.");
        }

        return _operationTypes.TryGetValue(operationName, out var type)
            ? (IOperation)Activator.CreateInstance(type)!
            : throw new ArgumentException(
                $"Package '{Manifest.Id}' {Manifest.Version} has no operation '{operationName}'.", nameof(operationName));
    }
}
