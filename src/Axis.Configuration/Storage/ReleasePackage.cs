namespace Axis.Configuration.Storage;

/// <summary>
/// One extension package a release pins: the package id and the exact installed version that
/// compilation picked from the application's range for it.
/// </summary>
public sealed class ReleasePackage
{
    public required Guid ReleaseId { get; init; }

    public required string PackageId { get; init; }

    public required string Version { get; init; }
}
