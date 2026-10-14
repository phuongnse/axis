namespace Axis.Configuration.Packages;

/// <summary>
/// An installed version of an extension package, by id and <c>major.minor.patch</c> version.
/// <see cref="Operations"/> holds the operations its manifest offers, and is empty when the
/// manifest is not known.
/// </summary>
public sealed record PackageVersion(string Id, string Version)
{
    public IReadOnlyList<PackageOperation> Operations { get; init; } = [];
}
