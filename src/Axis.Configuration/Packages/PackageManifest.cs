namespace Axis.Configuration.Packages;

/// <summary>A checked extension package manifest, <c>axis-package.json</c>.</summary>
/// <summary>Proposed: <c>Label</c> is the package's name for people, as a module's is; null in a manifest without one.</summary>
public sealed record PackageManifest(string Id, string Version, string Assembly, IReadOnlyList<PackageOperation> Operations, string? Label = null);
