using System.Globalization;
using System.Security.Claims;
using Axis.Core.Tenancy;
using Axis.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Axis.Server.Authentication;

/// <summary>
/// The cookie session of a person who signed in through their tenant's identity provider. The
/// cookie holds a protected ticket with the user id, the display name, the roles and the tenant id,
/// and no token. A session counts as no session on another tenant's host, after
/// <see cref="IdleTimeout"/> without requests, or <see cref="MaxLifetime"/> after sign-in.
/// </summary>
internal static class Session
{
    public const string Scheme = "Axis.Session";

    /// <summary>The ticket property that holds the sign-in time, kept across renewals.</summary>
    public const string SignedInAtItem = "axis:signed_in_at";

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

    /// <summary>
    /// Registers the session cookie. Outside Development and Testing the cookie is always Secure.
    /// Those two run over plain http, where a Secure cookie would never come back.
    /// </summary>
    public static AuthenticationBuilder AddSession(this AuthenticationBuilder builder, CookieSecurePolicy securePolicy)
    {
        builder.Services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        return builder.AddCookie(Scheme, options =>
        {
            options.Cookie.Name = Scheme;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = securePolicy;
            // Every request renews the session in OnValidatePrincipal, so the idle limit always
            // counts from the last request. Built-in sliding renewal waits for half the window.
            options.ExpireTimeSpan = IdleTimeout;
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
            options.Events.OnSigningIn = context =>
            {
                var now = context.Options.TimeProvider!.GetUtcNow();
                context.Properties.IssuedUtc = now;
                context.Properties.ExpiresUtc = now + IdleTimeout;
                context.Properties.Items.TryAdd(SignedInAtItem, now.ToString("O", CultureInfo.InvariantCulture));
                return Task.CompletedTask;
            };
            // The cookie handler has already rejected a ticket whose idle limit has passed.
            options.Events.OnValidatePrincipal = context =>
            {
                var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                var now = context.Options.TimeProvider!.GetUtcNow();
                if (context.Principal?.FindFirstValue(TenantIdClaim) is not { } tenantId
                    || tenantId != tenant?.TenantId
                    || !context.Properties.Items.TryGetValue(SignedInAtItem, out var stamp)
                    || !DateTimeOffset.TryParseExact(stamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var signedInAt)
                    || now >= signedInAt + MaxLifetime)
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
