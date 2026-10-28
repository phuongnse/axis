using System.Net;
using System.Text;

namespace Axis.Integration.Tests;

/// <summary>
/// Answers the calls that <c>TenantRealm</c> makes to the identity provider's admin API with success, so a test that only
/// needs a tenant does not create a realm in the shared Keycloak. It records the path of every request.
/// </summary>
internal sealed class StubIdentityProvider : HttpMessageHandler
{
    private readonly List<string> _paths = [];

    /// <summary>The path and query of every request, in order.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_paths)
            {
                return [.. _paths];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        lock (_paths)
        {
            _paths.Add(uri.PathAndQuery);
        }

        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(Json(Body(uri)));
        }

        if (uri.AbsolutePath.EndsWith("/openid-connect/token", StringComparison.Ordinal))
        {
            return Task.FromResult(Json("""{"access_token":"stub"}"""));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private static string Body(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith("/clients", StringComparison.Ordinal))
        {
            var clientId = Uri.UnescapeDataString(uri.Query.Replace("?clientId=", "", StringComparison.Ordinal));
            return $$"""[{"id":"{{clientId}}","clientId":"{{clientId}}"}]""";
        }

        return uri.AbsolutePath.EndsWith("/service-account-user", StringComparison.Ordinal)
            ? """{"id":"u"}"""
            : """{"name":"r","id":"r"}""";
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
