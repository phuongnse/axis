namespace Axis.Server.Authentication;

/// <summary>
/// A customer-hosted package runner that calls the runner's routes with the access token of its own client, see
/// <c>docs/reference/extension-packages.md#customer-hosted-runners</c>. A route opts in with <see cref="PackageRunnerRoute"/>. On such a
/// route the token is judged against the audience <c>axis-package-runner</c>, every request reads the runner's row, the session cookie is
/// never read or renewed, and no CSRF header is needed. Every other route refuses the token, because its audience is not the one the route
/// expects.
/// </summary>
internal static class PackageRunnerCaller
{
    /// <summary>Forwards to the package runner bearer scheme of the request's tenant.</summary>
    public const string Scheme = "Axis.PackageRunner";

    public const string SchemePrefix = "runner-";

    public static string SchemeName(string tenantId) => $"{SchemePrefix}{tenantId}";

    /// <summary>Whether the request's route carries <see cref="PackageRunnerRoute"/>.</summary>
    public static bool Applies(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<PackageRunnerRoute>() is not null;
}

/// <summary>
/// The endpoint metadata that marks a route of a package runner. The scheme choice, the CSRF exemption, the 401 answer and the route
/// tests all read it, so none of them matches paths.
/// </summary>
internal sealed class PackageRunnerRoute
{
    public static readonly PackageRunnerRoute Instance = new();

    private PackageRunnerRoute()
    {
    }
}
