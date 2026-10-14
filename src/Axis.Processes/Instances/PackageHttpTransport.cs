namespace Axis.Processes.Instances;

/// <summary>
/// The one connection pool behind every HTTP client a package operation gets. It follows no redirect,
/// because a redirect could leave the declared hosts. It keeps no cookies, so one tenant's cookies
/// never reach another tenant's requests on the shared pool.
/// </summary>
internal sealed class PackageHttpTransport : IDisposable
{
    /// <summary>The handler the clients send through. The clients do not dispose it.</summary>
    public SocketsHttpHandler Handler { get; } = new() { AllowAutoRedirect = false, UseCookies = false };

    public void Dispose() => Handler.Dispose();
}
