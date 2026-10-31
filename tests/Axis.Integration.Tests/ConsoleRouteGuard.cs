using System.Net;
using System.Text;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The checks that hold every route under <c>/api/console/</c> to the rules of the operator console: only the operator
/// role gets in, a write needs the CSRF header, and no read answers a value the console never shows. The routes are
/// read from the endpoint data source, so a route added later is checked without anyone listing it.
/// </summary>
internal static class ConsoleRouteGuard
{
    private const string Prefix = "/api/console/";

    private const string CsrfHeader = "X-Axis-Csrf";

    private static readonly HashSet<string> _writes = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Every route and method under <c>/api/console/</c>.</summary>
    public static IReadOnlyList<ApiRoute> Routes(IServiceProvider services) =>
        [.. ApiRoutes.From(services).Where(route => route.Pattern.RawText?.StartsWith(Prefix, StringComparison.Ordinal) == true)];

    /// <summary>
    /// Calls each route signed out (expects 401), as <paramref name="userWithoutRole"/> (expects 403) and, for a write, as
    /// <paramref name="operatorUser"/> without the CSRF header (expects 403). Returns one line for each call that answered
    /// something else.
    /// </summary>
    public static async Task<IReadOnlyList<string>> AccessFailuresAsync(
        IReadOnlyList<ApiRoute> routes,
        Func<HttpClient> createClient,
        string host,
        string operatorUser,
        string userWithoutRole,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var route in routes)
        {
            var path = route.PathWith(new Dictionary<string, string>());
            await ExpectAsync(route, path, null, csrf: true, "signed out", HttpStatusCode.Unauthorized);
            await ExpectAsync(route, path, userWithoutRole, csrf: true, "without the operator role", HttpStatusCode.Forbidden);
            if (_writes.Contains(route.Method))
            {
                await ExpectAsync(route, path, operatorUser, csrf: false, "without the CSRF header", HttpStatusCode.Forbidden);
            }
        }

        return failures;

        async Task ExpectAsync(ApiRoute route, string path, string? user, bool csrf, string caller, HttpStatusCode expected)
        {
            using var client = createClient();
            if (user is not null)
            {
                client.SignedInAs(user);
            }

            using var request = Request(route, path, host, csrf);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode != expected)
            {
                failures.Add($"{route.Method} {path} {caller} -> {(int)response.StatusCode}, expected {(int)expected}");
            }
        }
    }

    /// <summary>
    /// Calls each read as <paramref name="operatorUser"/>, with each path parameter filled from <paramref name="parameters"/> and
    /// <c>x</c> otherwise. Returns one line for each read that does not answer 200, and one for each of
    /// <paramref name="secrets"/> an answer holds.
    /// </summary>
    public static async Task<IReadOnlyList<string>> LeakFailuresAsync(
        IReadOnlyList<ApiRoute> routes,
        Func<HttpClient> createClient,
        string host,
        string operatorUser,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> secrets,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var route in routes.Where(route => route.Method == "GET"))
        {
            var path = route.PathWith(parameters);
            using var client = createClient().SignedInAs(operatorUser);
            using var request = Request(route, path, host, csrf: true);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                failures.Add($"{route.Method} {path} answered {(int)response.StatusCode}, not 200, so its answer was not checked: give the guard a value for each path parameter");
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            failures.AddRange(secrets
                .Where(secret => body.Contains(secret, StringComparison.Ordinal))
                .Select(secret => $"{route.Method} {path} answered the seeded value {secret}"));
        }

        return failures;
    }

    private static HttpRequestMessage Request(ApiRoute route, string path, string host, bool csrf)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        if (csrf)
        {
            request.Headers.Add(CsrfHeader, "1");
        }

        if (_writes.Contains(route.Method))
        {
            request.Content = new StringContent("""{"reason":"guard"}""", Encoding.UTF8, "application/json");
        }

        return request;
    }
}
