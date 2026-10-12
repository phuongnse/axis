using System.Security.Claims;
using Axis.Core.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Axis.Server.Authentication;

/// <summary>
/// Another system that calls an inbound endpoint with a bearer token from its tenant's identity
/// provider, through the client credentials grant. The server checks the token's signature, issuer,
/// audience and expiry against the tenant resolved from the host, with one JWT bearer scheme per
/// tenant. It accepts a token only on endpoint paths, and there it reads no session cookie.
/// </summary>
internal static class EndpointCaller
{
    /// <summary>Forwards to the bearer scheme of the request's tenant.</summary>
    public const string Scheme = "Axis.EndpointCaller";

    /// <summary>
    /// The default scheme of every request: the bearer token on endpoint paths, and the
    /// <see cref="Session"/> everywhere else.
    /// </summary>
    public const string RequestScheme = "Axis.Request";

    private const string CallerIdClaim = "axis:caller_id";
    private const string DisplayNameClaim = "axis:caller_display_name";
    private const string RoleClaim = "axis:caller_role";
    private const string TenantIdClaim = "axis:tenant_id";

    public static string SchemeName(string tenantId) => $"endpoint-{tenantId}";

    /// <summary>Whether the path is under <c>/api/apps/{app}/endpoints</c>, in any case.</summary>
    public static bool IsEndpointPath(PathString path)
    {
        if (!path.StartsWithSegments("/api/apps", StringComparison.OrdinalIgnoreCase, out var rest) || !rest.HasValue)
        {
            return false;
        }

        // The rest starts with a slash, so the segments are "", the app, then "endpoints".
        var segments = rest.Value.Split('/', 4);
        return segments.Length >= 3
            && segments[1].Length > 0
            && string.Equals(segments[2], "endpoints", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A principal with its own claim types, so <see cref="Session.Read"/> never takes a caller
    /// for a session user.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(string tenantId, string callerId, string displayName, IEnumerable<string> roles)
    {
        Claim[] claims =
        [
            new(CallerIdClaim, callerId),
            new(DisplayNameClaim, displayName),
            .. roles.Select(role => new Claim(RoleClaim, role)),
            new(TenantIdClaim, tenantId),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, CallerIdClaim, RoleClaim));
    }

    /// <summary>The endpoint caller, or null when the principal is not a caller.</summary>
    public static EndpointCallerIdentity? Read(ClaimsPrincipal principal) =>
        principal.FindFirstValue(CallerIdClaim) is { Length: > 0 } id
            ? new EndpointCallerIdentity(
                id,
                principal.FindFirstValue(DisplayNameClaim) ?? id,
                [.. principal.FindAll(RoleClaim).Select(claim => claim.Value)])
            : null;

    /// <summary>
    /// Registers the default request scheme, the endpoint caller scheme and one JWT bearer scheme
    /// per tenant. A tenant without <c>Oidc:EndpointAudience</c> accepts no bearer token.
    /// </summary>
    /// <param name="allowHttp">Whether the identity provider may be reached over http, in Development and Testing only.</param>
    public static AuthenticationBuilder AddEndpointCallers(
        this AuthenticationBuilder builder,
        IReadOnlyDictionary<string, TenantOidcSettings> tenants,
        bool allowHttp)
    {
        builder.AddPolicyScheme(RequestScheme, displayName: null, options =>
            options.ForwardDefaultSelector = context => IsEndpointPath(context.Request.Path) ? Scheme : Session.Scheme);

        // Tenant resolution answers a request without a tenant before authentication runs. Should one
        // get here anyway, the first tenant's scheme gives it no result, as it does any other tenant.
        builder.AddPolicyScheme(Scheme, displayName: null, options =>
            options.ForwardDefaultSelector = context =>
            {
                var current = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                return SchemeName(current?.TenantId ?? tenants.Keys.First());
            });

        foreach (var (tenantId, settings) in tenants)
        {
            builder.AddJwtBearer(SchemeName(tenantId), options =>
            {
                options.Authority = settings.Authority;
                options.Audience = settings.EndpointAudience;
                options.RequireHttpsMetadata = !allowHttp;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var current = context.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                        if (current?.TenantId != tenantId || string.IsNullOrWhiteSpace(settings.EndpointAudience))
                        {
                            context.NoResult();
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal;
                        var callerId = principal?.FindFirst(settings.Claims.UserId)?.Value;
                        if (principal is null || string.IsNullOrWhiteSpace(callerId))
                        {
                            context.Fail("The access token has no user id claim.");
                            return Task.CompletedTask;
                        }

                        // Service-account tokens often have no name, and a bare id is hard to read in a history.
                        var displayName = principal.FindFirst(settings.Claims.DisplayName)?.Value;
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            displayName = principal.FindFirst("azp")?.Value;
                        }

                        var roles = principal.FindAll(settings.Claims.Roles).Select(claim => claim.Value);
                        context.Principal = CreatePrincipal(
                            tenantId,
                            callerId,
                            string.IsNullOrWhiteSpace(displayName) ? callerId : displayName,
                            roles);
                        return Task.CompletedTask;
                    },
                };
            });

            // The expiry is checked against the server's clock, so tests can replace it. The
            // parameters keep their default clock skew of 5 minutes.
            builder.Services.AddOptions<JwtBearerOptions>(SchemeName(tenantId))
                .Configure<TimeProvider>((options, clock) =>
                    options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        return expires is { } expiresAt
                            && now < expiresAt.ToUniversalTime() + parameters.ClockSkew
                            && (notBefore is not { } notBeforeAt || now >= notBeforeAt.ToUniversalTime() - parameters.ClockSkew);
                    });
        }

        return builder;
    }
}

/// <summary>The id, display name and role names of a system that calls an inbound endpoint.</summary>
internal sealed record EndpointCallerIdentity(string Id, string DisplayName, IReadOnlyList<string> Roles);
