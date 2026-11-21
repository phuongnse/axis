namespace Axis.Integration.Tests;

/// <summary>
/// The classes that build a package in the package host run one after the other. Every build writes and deletes a folder
/// named <c>axis-package-build-*</c> in the temporary folder, and <see cref="ApplicationPackageBuildTests"/> lists them to see
/// that the host leaves none behind, so a build of another class running beside it would fail it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PackageBuilds
{
    public const string Name = "Package builds";
}
