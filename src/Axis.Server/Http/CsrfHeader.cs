using Axis.Server.Authentication;

namespace Axis.Server.Http;

/// <summary>The CSRF header rule from D23 for unsafe requests under <c>/api</c>.</summary>
internal static class CsrfHeader
{
    public const string Name = "X-Axis-Csrf";

    /// <summary>
    /// Refuses a <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c> under <c>/api</c> with a
    /// <c>403</c> unless it carries exactly one <c>X-Axis-Csrf: 1</c> header. A cross-site page
    /// cannot set a custom header without a CORS preflight, and Axis allows no cross-origin requests.
    /// Endpoint paths and service account requests are exempt: they take a bearer token and no cookie, and a
    /// browser cannot attach a bearer token on its own.
    /// </summary>
    public static IApplicationBuilder UseCsrfHeader(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments("/api")
                && !EndpointCaller.IsEndpointPath(request.Path)
                && !ServiceAccount.Applies(context)
                && IsUnsafe(request.Method)
                && !HasHeader(request))
            {
                await Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "The request must carry the X-Axis-Csrf header.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool HasHeader(HttpRequest request) =>
        request.Headers.TryGetValue(Name, out var values)
        && values.Count == 1
        && string.Equals(values[0], "1", StringComparison.Ordinal);
}
