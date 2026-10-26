using System.Security.Claims;
using Axis.Core.Tenancy;
using Axis.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Axis.Server.Authentication;

/// <summary>
/// The session of a person who signed in through their tenant's identity provider. The state is a
/// row of <c>axis.sessions</c> in the tenant database, with the user id, the display name, the roles and
/// the times of sign-in and of the last request, and no token. The cookie holds only the random session
/// id. A session counts as no session on another tenant's host, after <see cref="IdleTimeout"/> without
/// requests, or <see cref="MaxLifetime"/> after sign-in, and sign-out deletes the row at once.
/// </summary>
internal static class Session
{
    public const string Scheme = "Axis.Session";

    /// <summary>The cookie name outside Development and Testing. The <c>__Host-</c> prefix makes a browser accept it only with Secure, Path=/ and no domain.</summary>
    public const string HostCookieName = "__Host-Axis.Session";

    /// <summary>
    /// The request item that sign-in sets, so the session store refuses the session that the request's cookie
    /// names. The cookie handler then issues a new session id instead of renewing the old one.
    /// </summary>
    public const string SigningInItem = "axis:signing_in";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(1);

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);

    private const string UserIdClaim = "axis:user_id";
    private const string DisplayNameClaim = "axis:display_name";
    private const string RoleClaim = "axis:role";
    private const string TenantIdClaim = "axis:tenant_id";

    public static ClaimsPrincipal CreatePrincipal(string tenantId, string userId, string displayName, IEnumerable<string> roles)
    {
        Claim[] claims =
        [
            new(UserIdClaim, userId),
            new(DisplayNameClaim, displayName),
            .. roles.Select(role => new Claim(RoleClaim, role)),
            new(TenantIdClaim, tenantId),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, UserIdClaim, RoleClaim));
    }

    /// <summary>The session user, or null when the principal is not a session.</summary>
    public static CurrentUserResponse? Read(ClaimsPrincipal principal) =>
        principal.FindFirstValue(UserIdClaim) is { Length: > 0 } id
            ? new CurrentUserResponse(
                id,
                principal.FindFirstValue(DisplayNameClaim) ?? id,
                [.. principal.FindAll(RoleClaim).Select(claim => claim.Value)])
            : null;

    /// <summary>The tenant id of a session principal, or null when the principal is not a session.</summary>
    public static string? ReadTenantId(ClaimsPrincipal principal) => principal.FindFirstValue(TenantIdClaim);

    /// <summary>Whether the server may run over plain http: Development and Testing only.</summary>
    public static bool AllowsPlainHttp(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    /// <summary>
    /// Registers the session cookie and the store of its sessions. Outside Development and Testing the
    /// cookie is <see cref="HostCookieName"/> and always Secure. Those two run over plain http, where a
    /// Secure cookie would never come back, so they keep the name <see cref="Scheme"/> and send the
    /// cookie over the request's scheme.
    /// </summary>
    public static AuthenticationBuilder AddSession(this AuthenticationBuilder builder, bool plainHttpAllowed)
    {
        builder.Services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock)
            .Configure(options => options.SessionStore = new SessionStore());
        return builder.AddCookie(Scheme, options =>
        {
            options.Cookie.Name = plainHttpAllowed ? Scheme : HostCookieName;
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
            // The store has already refused a session past its limits.
            options.Events.OnValidatePrincipal = context =>
            {
                var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                if (context.Principal is not { } principal
                    || ReadTenantId(principal) is not { } tenantId
                    || tenantId != tenant?.TenantId)
                {
                    context.RejectPrincipal();
                    return Task.CompletedTask;
                }

                context.ShouldRenew = true;
                return Task.CompletedTask;
            };
        });
    }
}
