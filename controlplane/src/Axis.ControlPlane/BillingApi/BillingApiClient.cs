using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Axis.ControlPlane.BillingApi;

/// <summary>
/// The sign-in of the operator's billing system, see <c>docs/reference/billing-api.md#signing-in</c>. The billing system is a client of the operator's identity
/// provider and calls with a bearer token. The control plane checks the token's issuer, signature, expiry and audience against
/// <c>ControlPlane:Billing</c>, and that the token's <c>client_id</c> or <c>azp</c> is the configured client. The route policy then needs <c>axis-billing</c>
/// in the token's <c>scope</c>. A token that fails a check answers <c>401</c> and names no check, and a billing client's token without the scope answers <c>403</c>.
/// The principal has claim types of its own, so no other caller is taken for the billing client.
/// </summary>
internal static partial class BillingApiClient
{
    /// <summary>The JWT bearer scheme of the billing client.</summary>
    public const string Scheme = "Axis.Billing";

    /// <summary>The policy of the billing routes: an authenticated billing client whose token has the <see cref="RequiredScope"/>.</summary>
    public const string Policy = "Axis.Billing";

    /// <summary>The scope that every billing call needs.</summary>
    public const string RequiredScope = "axis-billing";

    /// <summary>The claim that holds the id of the billing client. It is the actor of the audit records.</summary>
    public const string ClientClaim = "axis:billing_client";

    /// <summary>The claim that holds each scope of the token.</summary>
    public const string ScopeClaim = "axis:billing_scope";

    // RSA, RSA-PSS and ECDSA, as the cell's bearer callers accept. HS* and none are refused.
    private static readonly string[] _algorithms = ["RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];

    [LoggerMessage(LogLevel.Warning, "A bearer token was refused on a billing route: {Reason}.")]
    private static partial void TokenRefused(ILogger logger, string reason);

    /// <summary>
    /// Registers the scheme and the policy. Without complete <paramref name="billing"/> settings no scheme is registered and the routes answer
    /// <c>401</c>, see <see cref="BillingApiEndpoints"/>.
    /// </summary>
    public static IServiceCollection AddBillingApiClient(this IServiceCollection services, ControlPlaneBillingOptions billing)
    {
        ArgumentNullException.ThrowIfNull(billing);
        var authentication = services.AddAuthentication();
        if (billing.IsComplete)
        {
            authentication.AddJwtBearer(Scheme, options =>
            {
                options.Authority = billing.Authority;
                options.Audience = billing.Audience;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidAlgorithms = _algorithms;
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        TokenRefused(Logger(context.HttpContext), context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        if (context.SecurityToken is not JsonWebToken token || ClientId(token) != billing.ClientId)
                        {
                            TokenRefused(Logger(context.HttpContext), "The token is not the billing client's.");
                            context.Fail("The token is not the billing client's.");
                            return Task.CompletedTask;
                        }

                        context.Principal = CreatePrincipal(billing.ClientId!, Scopes(token));
                        return Task.CompletedTask;
                    },
                };
            });
        }

        services.AddAuthorization(options => options.AddPolicy(
            Policy,
            policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim(ScopeClaim, RequiredScope)));
        return services;
    }

    /// <summary>A principal with the billing client's own claim types.</summary>
    internal static ClaimsPrincipal CreatePrincipal(string clientId, IEnumerable<string> scopes)
    {
        Claim[] claims = [new(ClientClaim, clientId), .. scopes.Select(scope => new Claim(ScopeClaim, scope))];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClientClaim, null));
    }

    private static string? ClientId(JsonWebToken token) =>
        token.TryGetPayloadValue<string>("client_id", out var clientId) && !string.IsNullOrWhiteSpace(clientId)
            ? clientId
            : token.TryGetPayloadValue<string>("azp", out var authorizedParty) && !string.IsNullOrWhiteSpace(authorizedParty)
                ? authorizedParty
                : null;

    // The scope claim is one string of scopes that a space separates.
    private static string[] Scopes(JsonWebToken token) =>
        token.TryGetPayloadValue<string>("scope", out var scope) && !string.IsNullOrWhiteSpace(scope)
            ? scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BillingApiClient).FullName!);
}
