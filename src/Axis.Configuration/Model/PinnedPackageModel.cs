namespace Axis.Configuration.Model;

/// <summary>
/// An extension package an application uses: its id, the version range as written, and the exact
/// installed version that compilation picked from that range. <paramref name="CustomerHosted"/> is true when the application
/// runs the package in a customer-hosted runner, see
/// <c>docs/reference/extension-packages.md#customer-hosted-runners</c>.
/// </summary>
public sealed record PinnedPackageModel(string Id, string Range, string Version, bool CustomerHosted = false);
