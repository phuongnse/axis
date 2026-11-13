namespace Axis.Configuration.Packages;

/// <summary>How <see cref="PackageCatalog.Load"/> reads the assembly of a C# package, see <c>docs/reference/extension-packages.md#loading</c>.</summary>
public enum PackageLoadMode
{
    /// <summary>Loads each package into its own <see cref="PackageLoadContext"/>, so its operations can run.</summary>
    Run,

    /// <summary>Reads each package as metadata only. No package code runs, so the packages cannot create operations.</summary>
    MetadataOnly,
}
