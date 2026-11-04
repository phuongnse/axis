using System.Security.Claims;
using System.Text.Json;
using Axis.Server.Authentication;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Axis.Server.CellApi;

/// <summary>
/// A machine caller of the cell's <see cref="CellOptions.OperatorRealm"/>, such as the control plane or a script, that calls
/// the provisioning API under <c>/api/cell/</c> with a bearer token. The server finds the realm at <c>IdentityProvider:Url</c>
/// plus <c>realms/&lt;Cell:OperatorRealm&gt;</c> and checks the token's issuer, signature, expiry and type. It checks no
/// audience, because no setting names one for the operator realm and the realm serves only the cell. The roles come from
/// Keycloak's <c>realm_access.roles</c> claim, and the route policy needs <see cref="CellOptions.OperatorRole"/> among them.
/// A route opts in with <see cref="CellOperatorRoute"/>, and the scheme choice reads that marker, never the path. The
/// principal has its own claim types, so <see cref="Session.Read"/> and <see cref="EndpointCaller.Read"/> never take an
/// operator for a session user or an endpoint caller.
/// </summary>
internal static partial class CellOperator
{
    /// <summary>The JWT bearer scheme of the operator realm.</summary>
    public const string Scheme = "Axis.CellOperator";

    /// <summary>The policy of the routes under <c>/api/cell/</c>: an authenticated operator with the operator role.</summary>
    public const string Policy = "Axis.CellOperator";

    /// <summary>The setting that holds the address of the identity provider.</summary>
    public const string IdentityProviderUrlSetting = "IdentityProvider:Url";

    /// <summary>The claim that holds each role of the operator.</summary>
    public const string RoleClaim = "axis:operator_role";

    private const string IdClaim = "axis:operator_id";
    private const string DisplayNameClaim = "axis:operator_display_name";

    [LoggerMessage(LogLevel.Warning, "A bearer token was refused on a cell route: {Reason}.")]
    private static partial void TokenRefused(ILogger logger, string reason);

    /// <summary>
    /// The address of the operator realm, or null when the host has no platform host and so no operator caller. A
    /// self-hosted installation, and a cell with neither <c>Cell:ConsoleHost</c> nor <c>Cell:InternalUrl</c>, have none.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>IdentityProvider:Url</c> is missing, is not an absolute URL, or is not <c>https</c> outside Development and Testing.
    /// The message names the setting and never its value.
    /// </exception>
    public static Uri? Load(IConfiguration configuration, CellOptions cell, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(environment);

        if (!cell.IsCell || !new CellHosts(cell).Any)
        {
            return null;
        }

        var url = configuration[IdentityProviderUrlSetting];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"The setting {IdentityProviderUrlSetting} is required when Cell:ConsoleHost or Cell:InternalUrl is set.");
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"The setting {IdentityProviderUrlSetting} must be an absolute http or https URL.");
        }

        if (address.Scheme != Uri.UriSchemeHttps && !Session.AllowsPlainHttp(environment))
        {
            throw new InvalidOperationException($"The setting {IdentityProviderUrlSetting} must be an https URL.");
        }

        var root = address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
        return new Uri(root, "realms/" + Uri.EscapeDataString(cell.OperatorRealm));
    }

    /// <summary>A principal with the operator's own claim types, never a session's or an endpoint caller's.</summary>
    public static ClaimsPrincipal CreatePrincipal(string id, string displayName, IEnumerable<string> roles)
    {
        Claim[] claims =
        [
            new(IdClaim, id),
            new(DisplayNameClaim, displayName),
            .. roles.Select(role => new Claim(RoleClaim, role)),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, IdClaim, RoleClaim));
    }

    /// <summary>
    /// Registers the JWT bearer scheme of the operator realm. Without an <paramref name="authority"/> the host has no
    /// platform host, so no request reaches a cell route and no scheme is registered.
    /// </summary>
    public static AuthenticationBuilder AddCellOperator(this AuthenticationBuilder builder, Uri? authority, bool allowHttp) =>
        authority is null ? builder : builder.AddJwtBearer(Scheme, options =>
        {
            options.Authority = authority.AbsoluteUri;
            options.RequireHttpsMetadata = !allowHttp;
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.SaveToken = false;
            options.TokenValidationParameters.ValidateAudience = false;
            options.TokenValidationParameters.ValidAlgorithms = BearerCallerOptions.Algorithms;
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    TokenRefused(Logger(context.HttpContext), context.Exception.GetType().Name);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    void Refuse(string reason)
                    {
                        TokenRefused(Logger(context.HttpContext), reason);
                        context.Fail(reason);
                    }

                    if (context.SecurityToken is not JsonWebToken token || !BearerCallerOptions.IsAccessToken(token))
                    {
                        Refuse("The token is not an access token.");
                        return Task.CompletedTask;
                    }

                    var id = token.Subject;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Refuse("The access token has no subject.");
                        return Task.CompletedTask;
                    }

                    var displayName = token.TryGetPayloadValue<string>("azp", out var client) && !string.IsNullOrWhiteSpace(client)
                        ? client
                        : id;
                    context.Principal = CreatePrincipal(id, displayName, RealmRoles(token));
                    return Task.CompletedTask;
                },
            };
        });

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CellOperator).FullName!);

    /// <summary>The realm roles in the <c>realm_access.roles</c> claim, or none when the claim is missing or has another shape.</summary>
    private static IEnumerable<string> RealmRoles(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<JsonElement>("realm_access", out var access)
            || access.ValueKind != JsonValueKind.Object
            || !access.TryGetProperty("roles", out var roles)
            || roles.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. roles.EnumerateArray().Where(role => role.ValueKind == JsonValueKind.String).Select(role => role.GetString()!)];
    }
}

/// <summary>
/// The endpoint metadata that marks a route of the provisioning API. The scheme choice, the tenant resolution of a platform
/// host and the route test all read it, so none of them matches paths.
/// </summary>
internal sealed class CellOperatorRoute
{
    public static readonly CellOperatorRoute Instance = new();

    private CellOperatorRoute()
    {
    }
}
