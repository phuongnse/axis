using System.Security.Claims;
using System.Text.Json;
using Axis.Core.Tenancy;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Axis.Server.Authentication;

/// <summary>
/// Another system that calls an inbound endpoint with a bearer token from its tenant's identity
/// provider, through the client credentials grant. The server checks the token's signature, issuer,
/// audience and expiry against the tenant resolved from the host, with one JWT bearer scheme per
/// tenant. It accepts a token only on endpoint paths, and there it reads no session cookie. The routes of the record API,
/// the data source rows, process start and process inspection take a bearer token too, through
/// <see cref="ServiceAccount"/>. A tenant under <c>fapi2</c> accepts only a token that is bound to a DPoP key, sent as
/// <c>Authorization: DPoP</c> with a proof (see <see cref="DpopProof"/>).
/// </summary>
internal static class EndpointCaller
{
    /// <summary>Forwards to the bearer scheme of the request's tenant.</summary>
    public const string Scheme = "Axis.EndpointCaller";

    /// <summary>
    /// The default scheme of every request: the bearer token on endpoint paths, the service account's bearer token on a
    /// service account route, and the <see cref="Session"/> everywhere else.
    /// </summary>
    public const string RequestScheme = "Axis.Request";

    private const string CallerIdClaim = "axis:caller_id";
    private const string DisplayNameClaim = "axis:caller_display_name";
    private const string RoleClaim = "axis:caller_role";
    private const string TenantIdClaim = "axis:tenant_id";

    public const string SchemePrefix = "endpoint-";

    public static string SchemeName(string tenantId) => $"{SchemePrefix}{tenantId}";

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
    /// Registers the default request scheme, the endpoint caller and service account schemes and two JWT bearer
    /// schemes for each tenant in <paramref name="settings"/>, one for each audience. A refresh of the tenant directory
    /// adds and removes bearer schemes later through <see cref="TenantSignInSchemes"/>. A tenant without
    /// <c>Oidc:EndpointAudience</c> accepts no endpoint caller token, and one without <c>Oidc:ServiceAudience</c>
    /// accepts no service account token.
    /// </summary>
    /// <param name="allowHttp">Whether the identity provider may be reached over http, in Development and Testing only.</param>
    public static AuthenticationBuilder AddEndpointCallers(
        this AuthenticationBuilder builder,
        TenantSignInSettings settings,
        bool allowHttp)
    {
        builder.Services.TryAddSingleton(settings);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, BearerCallerOptions>(
            sp => new BearerCallerOptions(settings, allowHttp, sp.GetRequiredService<TimeProvider>())));

        // A tenant added at a refresh has no AddJwtBearer call, so the post-configuration is registered here.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, JwtBearerPostConfigureOptions>());

        builder.AddPolicyScheme(RequestScheme, displayName: null, options =>
            options.ForwardDefaultSelector = context =>
                IsEndpointPath(context.Request.Path) ? Scheme
                : ServiceAccount.Applies(context) ? ServiceAccount.Scheme
                : Session.Scheme);

        // Tenant resolution answers a request without a tenant before authentication runs, so one should
        // never get here without a tenant.
        builder.AddPolicyScheme(Scheme, displayName: null, options =>
            options.ForwardDefaultSelector = context =>
            {
                var current = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current
                    ?? throw new InvalidOperationException("No tenant context is set.");
                return SchemeName(current.TenantId);
            });

        builder.AddPolicyScheme(ServiceAccount.Scheme, displayName: null, options =>
            options.ForwardDefaultSelector = context =>
            {
                var current = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current
                    ?? throw new InvalidOperationException("No tenant context is set.");
                return ServiceAccount.SchemeName(current.TenantId);
            });

        foreach (var tenantId in settings.Current.Keys)
        {
            builder.AddScheme<JwtBearerOptions, JwtBearerHandler>(SchemeName(tenantId), displayName: null, configureOptions: null);
            builder.AddScheme<JwtBearerOptions, JwtBearerHandler>(ServiceAccount.SchemeName(tenantId), displayName: null, configureOptions: null);
        }

        return builder;
    }
}

/// <summary>
/// Builds the options of a tenant's bearer schemes from its settings, each time the options are created. The endpoint
/// caller scheme expects <c>Oidc:EndpointAudience</c> and the service account scheme <c>Oidc:ServiceAudience</c>. The
/// token checks and the claim mapping are the same for both.
/// </summary>
internal sealed class BearerCallerOptions(TenantSignInSettings settings, bool allowHttp, TimeProvider clock)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    private const string DpopScheme = "DPoP ";

    /// <summary>The thumbprint in the token's <c>cnf.jkt</c> claim, or null when the token is not bound to a key.</summary>
    private static string? BoundThumbprint(JsonWebToken token) =>
        token.TryGetPayloadValue<JsonElement>("cnf", out var cnf)
        && cnf.ValueKind == JsonValueKind.Object
        && cnf.TryGetProperty("jkt", out var jkt)
        && jkt.ValueKind == JsonValueKind.String
            ? jkt.GetString()
            : null;

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is null)
        {
            return;
        }

        string prefix;
        if (name.StartsWith(EndpointCaller.SchemePrefix, StringComparison.Ordinal))
        {
            prefix = EndpointCaller.SchemePrefix;
        }
        else if (name.StartsWith(ServiceAccount.SchemePrefix, StringComparison.Ordinal))
        {
            prefix = ServiceAccount.SchemePrefix;
        }
        else
        {
            return;
        }

        var tenantId = name[prefix.Length..];
        if (settings.Find(tenantId) is not { } tenant)
        {
            return;
        }

        var audience = prefix == ServiceAccount.SchemePrefix ? tenant.ServiceAudience : tenant.EndpointAudience;
        var dpopRequired = tenant.Profile == TenantSignInProfiles.Fapi2;
        options.Authority = tenant.Authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = !allowHttp;
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var current = context.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                if (current?.TenantId != tenantId || string.IsNullOrWhiteSpace(audience))
                {
                    context.NoResult();
                    return Task.CompletedTask;
                }

                // Under fapi2 a token is read only from "Authorization: DPoP", so a bound token never works as a plain
                // bearer token. The header's scheme is the first word and is matched in any letter case.
                if (dpopRequired)
                {
                    var header = context.Request.Headers.Authorization.ToString();
                    if (header.StartsWith(DpopScheme, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = header[DpopScheme.Length..].Trim();
                    }
                    else
                    {
                        context.Fail("A DPoP-bound token is required.");
                    }
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                if (dpopRequired)
                {
                    // The proof is made for the token as the caller sent it, and the token holds the key's thumbprint.
                    if (context.SecurityToken is not JsonWebToken token
                        || !await DpopProof.IsValidAsync(context.Request, token.EncodedToken, BoundThumbprint(token), clock.GetUtcNow()))
                    {
                        context.Fail("The access token is not bound to a valid DPoP proof.");
                        return;
                    }
                }

                var principal = context.Principal;
                var callerId = principal?.FindFirst(tenant.Claims.UserId)?.Value;
                if (principal is null || string.IsNullOrWhiteSpace(callerId))
                {
                    context.Fail("The access token has no user id claim.");
                    return;
                }

                // Service-account tokens often have no name, and a bare id is hard to read in a history.
                var displayName = principal.FindFirst(tenant.Claims.DisplayName)?.Value;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = principal.FindFirst("azp")?.Value;
                }

                var roles = principal.FindAll(tenant.Claims.Roles).Select(claim => claim.Value);
                context.Principal = EndpointCaller.CreatePrincipal(
                    tenantId,
                    callerId,
                    string.IsNullOrWhiteSpace(displayName) ? callerId : displayName,
                    roles);
            },
        };

        // The expiry is checked against the server's clock, so tests can replace it. The
        // parameters keep their default clock skew of 5 minutes.
        options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            return expires is { } expiresAt
                && now < expiresAt.ToUniversalTime() + parameters.ClockSkew
                && (notBefore is not { } notBeforeAt || now >= notBeforeAt.ToUniversalTime() - parameters.ClockSkew);
        };
    }
}

/// <summary>The id, display name and role names of a system that calls an inbound endpoint.</summary>
internal sealed record EndpointCallerIdentity(string Id, string DisplayName, IReadOnlyList<string> Roles);
