using Axis.Server.Authentication;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Axis.Server.CellApi;

/// <summary>
/// The session of a platform operator who signed in to the cell console through the operator realm, see
/// <see cref="CellConsoleSignIn"/>. The state is a row of <c>platform.operator_sessions</c> in the platform database, because
/// the session belongs to no tenant. It holds the operator's id, display name and roles and the times of sign-in and of the
/// last request, and no token. The cookie holds only the random session id. A session counts as no session on any host but
/// <see cref="CellOptions.ConsoleHost"/>, after <see cref="IdleTimeout"/> without requests, or <see cref="MaxLifetime"/> after
/// sign-in, and sign-out deletes the row at once. The principal has the claim types of <see cref="CellOperator"/>, so one
/// policy covers the session and the bearer token.
/// </summary>
internal static class CellOperatorSession
{
    public const string Scheme = "Axis.CellOperatorSession";

    /// <summary>The cookie name outside Development and Testing. The <c>__Host-</c> prefix makes a browser accept it only with Secure, Path=/ and no domain.</summary>
    public const string HostCookieName = "__Host-Axis.CellSession";

    /// <summary>The cookie name in Development and Testing, which run over plain http.</summary>
    public const string PlainCookieName = "Axis.CellSession";

    /// <summary>
    /// The request item that sign-in sets, so the session store refuses the session that the request's cookie
    /// names. The cookie handler then issues a new session id instead of renewing the old one.
    /// </summary>
    public const string SigningInItem = "axis:cell_signing_in";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Registers the session cookie and the store of its sessions. Without an <paramref name="authority"/> the host has no
    /// platform host, so no request reaches a cell route and no scheme is registered. Outside Development and Testing the cookie is
    /// <see cref="HostCookieName"/> and always Secure. Those two run over plain http, where a Secure cookie would never
    /// come back, so they use <see cref="PlainCookieName"/> and send the cookie over the request's scheme.
    /// </summary>
    public static AuthenticationBuilder AddCellOperatorSession(this AuthenticationBuilder builder, Uri? authority, bool plainHttpAllowed)
    {
        if (authority is null)
        {
            return builder;
        }

        builder.Services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock)
            .Configure(options => options.SessionStore = new CellOperatorSessionStore());
        return builder.AddCookie(Scheme, options =>
        {
            options.Cookie.Name = plainHttpAllowed ? PlainCookieName : HostCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // A PathBase would otherwise become the cookie's path, which a __Host- cookie must not have.
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = plainHttpAllowed ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            // The store enforces both limits from the row. Every request renews the cookie in
            // OnValidatePrincipal, and built-in sliding renewal would only wait for half the window.
            options.SlidingExpiration = false;
            // An API answers with a status code, never a redirect to a sign-in page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            // The store has already refused a session past its limits. A session counts only on the console host.
            options.Events.OnValidatePrincipal = context =>
            {
                var consoleHost = context.HttpContext.RequestServices.GetRequiredService<CellOptions>().ConsoleHost;
                if (context.Principal is null || !IsConsoleHost(context.HttpContext, consoleHost))
                {
                    context.RejectPrincipal();
                    return Task.CompletedTask;
                }

                context.ShouldRenew = true;
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>Whether the request came on <paramref name="consoleHost"/>, in any letter case.</summary>
    public static bool IsConsoleHost(HttpContext context, string? consoleHost) =>
        !string.IsNullOrWhiteSpace(consoleHost)
        && string.Equals(context.Request.Host.Host, consoleHost.Trim(), StringComparison.OrdinalIgnoreCase);
}
