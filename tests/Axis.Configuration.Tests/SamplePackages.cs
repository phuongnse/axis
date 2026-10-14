using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

/// <summary>
/// The sample extension package that <c>samples/packages/SamplePackages.targets</c> copies to the test
/// output, which the purchase request sample declares.
/// </summary>
internal static class SamplePackages
{
    private static readonly Lazy<IReadOnlyList<PackageVersion>> _versions = new(() => PackageCatalog.Load(Path).Versions);

    /// <summary>The folder that acts as <c>Extensions:Path</c>.</summary>
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "SamplePackages");

    /// <summary>The loaded versions, to compile the sample against.</summary>
    public static IReadOnlyList<PackageVersion> Versions => _versions.Value;
}
