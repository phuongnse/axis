namespace Axis.Configuration.Packages;

/// <summary>A checked extension package manifest, <c>axis-package.json</c>.</summary>
public sealed record PackageManifest(string Id, string Version, string Label, string Assembly, IReadOnlyList<PackageOperation> Operations);
