using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>
/// An installed extension package version whose manifest matches its assembly. It is loaded in its own
/// context, or, when the catalog only read its metadata, it has no code that can run.
/// </summary>
public sealed class InstalledPackage
{
    private readonly Dictionary<string, Type> _operationTypes;

    internal InstalledPackage(PackageManifest manifest, PackageLoadContext? loadContext, Dictionary<string, Type> operationTypes)
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

    /// <summary>
    /// The context the assembly is loaded in, or null for a <c>node</c> or <c>python</c> package, which has no
    /// assembly, and for a package that was read as metadata only.
    /// </summary>
    public PackageLoadContext? LoadContext { get; }

    /// <summary>Creates a new instance of the operation named <paramref name="operationName"/>, ignoring letter case.</summary>
    /// <exception cref="ArgumentException">The package has no such operation.</exception>
    /// <exception cref="InvalidOperationException">The package is a <c>node</c> or <c>python</c> package, or was read as metadata only.</exception>
    public IOperation CreateOperation(string operationName)
    {
        if (LoadContext is null)
        {
            throw new InvalidOperationException(
                Manifest.Language == PackageLanguage.CSharp
                    ? $"Package '{Manifest.Id}' {Manifest.Version} was read as metadata only and cannot run."
                    : $"Package '{Manifest.Id}' {Manifest.Version} is a '{Manifest.Language.ToString().ToLowerInvariant()}' package, whose code runs only in a package runner.");
        }

        return _operationTypes.TryGetValue(operationName, out var type)
            ? (IOperation)Activator.CreateInstance(type)!
            : throw new ArgumentException(
                $"Package '{Manifest.Id}' {Manifest.Version} has no operation '{operationName}'.", nameof(operationName));
    }
}
