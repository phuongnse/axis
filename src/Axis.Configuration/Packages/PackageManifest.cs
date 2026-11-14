namespace Axis.Configuration.Packages;

/// <summary>
/// A checked extension package manifest, <c>axis-package.json</c>. <paramref name="CustomerHosted"/> is true when it says
/// <c>"runner": "customer"</c>, see <c>docs/reference/extension-packages.md#manifest</c>.
/// </summary>
public sealed record PackageManifest(
    string Id,
    string Version,
    string Label,
    PackageLanguage Language,
    string? Assembly,
    string? Adapter,
    string Lock,
    IReadOnlyList<PackageOperation> Operations,
    bool CustomerHosted = false);
