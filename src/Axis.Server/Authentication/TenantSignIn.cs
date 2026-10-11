using Axis.Core.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Axis.Server.Authentication;

/// <summary>
/// One OIDC client per tenant, using the code flow with PKCE. All of them share the callback path,
/// and each one skips a callback that arrives on another tenant's host, so a callback can never
/// complete sign-in for the wrong tenant. After the callback the server keeps no token: it reads
/// the configured claims from the ID token into a <see cref="Session"/>.
/// </summary>
internal static class TenantSignIn
{
    public const string CallbackPath = "/api/auth/callback";

    public static string SchemeName(string tenantId) => $"oidc-{tenantId}";

    /// <param name="securePolicy">The policy of the helper cookies, the same as the session cookie's.</param>
    /// <param name="allowHttp">Whether the identity provider may be reached over http, in Development and Testing only.</param>
    public static AuthenticationBuilder AddTenantSignIn(
        this AuthenticationBuilder builder,
        IReadOnlyDictionary<string, TenantOidcSettings> tenants,
        CookieSecurePolicy securePolicy,
        bool allowHttp)
    {
        foreach (var (tenantId, settings) in tenants)
        {
            builder.AddOpenIdConnect(SchemeName(tenantId), options =>
            {
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.RequireHttpsMetadata = !allowHttp;
                options.ResponseType = "code";
                // The identity provider returns with a GET. A cross-site form post would not carry
                // the SameSite=Lax correlation and nonce cookies.
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                // D9 leaves pushed authorization requests (PAR) to a later FAPI 2.0 milestone, so the
                // authorization request, PKCE included, goes in the redirect.
                options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
                options.SaveTokens = false;
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.UseTokenLifetime = false;
                options.CallbackPath = CallbackPath;
                // Sign-out goes through POST /api/auth/sign-out, so the handler serves no sign-out paths.
                options.SignedOutCallbackPath = PathString.Empty;
                options.RemoteSignOutPath = PathString.Empty;
                options.SignInScheme = Session.Scheme;
                // The defaults are Always and None, and a None cookie must be Secure, which breaks the flow over http.
                options.CorrelationCookie.SecurePolicy = securePolicy;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = securePolicy;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.Events.OnMessageReceived = context =>
                {
                    var current = context.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                    if (current?.TenantId != tenantId)
                    {
                        context.SkipHandler();
                    }

                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = context =>
                {
                    var principal = context.Principal;
                    var userId = principal?.FindFirst(settings.Claims.UserId)?.Value;
                    if (principal is null || string.IsNullOrWhiteSpace(userId))
                    {
                        context.Fail("The ID token has no user id claim.");
                        return Task.CompletedTask;
                    }

                    var displayName = principal.FindFirst(settings.Claims.DisplayName)?.Value;
                    var roles = principal.FindAll(settings.Claims.Roles).Select(claim => claim.Value);
                    context.Principal = Session.CreatePrincipal(
                        tenantId,
                        userId,
                        string.IsNullOrWhiteSpace(displayName) ? userId : displayName,
                        roles);
                    return Task.CompletedTask;
                };
            });
        }

        return builder;
    }
}
