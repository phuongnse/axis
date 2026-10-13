namespace Axis.Configuration.Packages;

/// <summary>The <c>url</c> and <c>secret</c> bindings an operation may ask its context for. Both are empty when absent.</summary>
public sealed record PackageCapabilities(IReadOnlyList<string> Urls, IReadOnlyList<string> Secrets);
