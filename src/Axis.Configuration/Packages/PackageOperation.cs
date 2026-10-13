namespace Axis.Configuration.Packages;

/// <summary>
/// An operation that an extension package offers. <see cref="Type"/> is the full name of the class
/// in the package's main assembly that implements <c>IOperation</c>. <see cref="Timeout"/> is 30
/// seconds when the manifest gives none.
/// </summary>
public sealed record PackageOperation(
    string Name,
    string Type,
    bool Idempotent,
    TimeSpan Timeout,
    IReadOnlyList<PackageField> Input,
    IReadOnlyList<PackageField> Output,
    PackageCapabilities Capabilities);
