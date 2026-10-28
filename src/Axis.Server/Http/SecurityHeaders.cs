namespace Axis.Server.Http;

/// <summary>The security headers of D85, set on every response the server writes.</summary>
internal static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; "
        + "font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// Sets the security headers on every response, including error answers, <c>401</c>, <c>403</c> and the tenant
    /// <c>404</c>. The headers are written when the response starts, so the forwarded headers have set the scheme by
    /// then. <c>Strict-Transport-Security</c> goes out only over <c>https</c> and only when
    /// <paramref name="strictTransportSecurity"/> is set. Every answer under <c>/api</c> gets <c>Cache-Control: no-store</c>,
    /// replacing any value an endpoint set. Text, JSON and XML answers that name no charset get <c>charset=utf-8</c>,
    /// except attachments, see <see cref="ResponseCharset"/>.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool strictTransportSecurity) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var request = context.Request;
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Permissions-Policy"] = PermissionsPolicy;
                if (strictTransportSecurity && request.IsHttps)
                {
                    headers.StrictTransportSecurity = StrictTransportSecurity;
                }

                var contentType = ResponseCharset.Apply(
                    headers.ContentType,
                    headers.ContentDisposition.ToString().StartsWith("attachment", StringComparison.OrdinalIgnoreCase));
                if (contentType != headers.ContentType)
                {
                    headers.ContentType = contentType;
                }

                if (request.Path.StartsWithSegments("/api"))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
}
