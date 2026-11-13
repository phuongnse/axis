using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>
/// An installed extension package version whose manifest matches its assembly. It is loaded in its own
/// context, or, when the catalog only read its metadata, it has no code that can run.
/// </summary>
public sealed class InstalledPackage
{
    private readonly Dictionary<string, Type> _operationTypes;

    internal InstalledPackage(PackageManifest manifest, PackageLoadContext loadContext, Dictionary<string, Type> operationTypes)
    {
        Manifest = manifest;
        LoadContext = loadContext;
        _operationTypes = operationTypes;
    }

    /// <summary>A package that was read as metadata only, see <see cref="PackageLoadMode.MetadataOnly"/>.</summary>
    internal InstalledPackage(PackageManifest manifest)
    {
        Manifest = manifest;
        _operationTypes = [];
    }

    public PackageManifest Manifest { get; }

    /// <summary>The load context that runs the package, or null when the package was read as metadata only.</summary>
    public PackageLoadContext? LoadContext { get; }

    /// <summary>Creates a new instance of the operation named <paramref name="operationName"/>, ignoring letter case.</summary>
    /// <exception cref="ArgumentException">The package has no such operation.</exception>
    /// <exception cref="InvalidOperationException">The package was read as metadata only and cannot run.</exception>
    public IOperation CreateOperation(string operationName)
    {
        if (LoadContext is null)
        {
            throw new InvalidOperationException($"Package '{Manifest.Id}' {Manifest.Version} was read as metadata only and cannot run.");
        }

        return _operationTypes.TryGetValue(operationName, out var type)
            ? (IOperation)Activator.CreateInstance(type)!
            : throw new ArgumentException(
                $"Package '{Manifest.Id}' {Manifest.Version} has no operation '{operationName}'.", nameof(operationName));
    }
}
