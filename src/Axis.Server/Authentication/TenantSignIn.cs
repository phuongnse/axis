using Axis.Core.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

    public const string SchemePrefix = "oidc-";

    public static string SchemeName(string tenantId) => $"{SchemePrefix}{tenantId}";

    /// <summary>
    /// Registers the scheme of each tenant in <paramref name="settings"/>. A refresh of the tenant directory adds and
    /// removes schemes later through <see cref="TenantSignInSchemes"/>.
    /// </summary>
    /// <param name="securePolicy">The policy of the helper cookies, the same as the session cookie's.</param>
    /// <param name="allowHttp">Whether the identity provider may be reached over http, in Development and Testing only.</param>
    public static AuthenticationBuilder AddTenantSignIn(
        this AuthenticationBuilder builder,
        TenantSignInSettings settings,
        CookieSecurePolicy securePolicy,
        bool allowHttp)
    {
        builder.Services.TryAddSingleton(settings);
        builder.Services.TryAddSingleton<TenantSignInSchemes>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIdConnectOptions>, TenantSignInOptions>(
            _ => new TenantSignInOptions(settings, securePolicy, allowHttp)));

        // A tenant added at a refresh has no AddOpenIdConnect call, so the post-configuration is registered here.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        foreach (var tenantId in settings.Current.Keys)
        {
            builder.AddScheme<OpenIdConnectOptions, OpenIdConnectHandler>(SchemeName(tenantId), displayName: null, configureOptions: null);
        }

        return builder;
    }
}

/// <summary>Builds the options of a tenant's OIDC client from its settings, each time the options are created.</summary>
internal sealed class TenantSignInOptions(TenantSignInSettings settings, CookieSecurePolicy securePolicy, bool allowHttp)
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name is null || !name.StartsWith(TenantSignIn.SchemePrefix, StringComparison.Ordinal))
        {
            return;
        }

        var tenantId = name[TenantSignIn.SchemePrefix.Length..];
        if (settings.Find(tenantId) is not { } tenant)
        {
            return;
        }

        options.Authority = tenant.Authority;
        options.ClientId = tenant.ClientId;
        options.ClientSecret = tenant.ClientSecret?.Reveal();
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
        options.CallbackPath = TenantSignIn.CallbackPath;
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
            var userId = principal?.FindFirst(tenant.Claims.UserId)?.Value;
            if (principal is null || string.IsNullOrWhiteSpace(userId))
            {
                context.Fail("The ID token has no user id claim.");
                return Task.CompletedTask;
            }

            var displayName = principal.FindFirst(tenant.Claims.DisplayName)?.Value;
            var roles = principal.FindAll(tenant.Claims.Roles).Select(claim => claim.Value);
            context.Principal = Session.CreatePrincipal(
                tenantId,
                userId,
                string.IsNullOrWhiteSpace(displayName) ? userId : displayName,
                roles);
            return Task.CompletedTask;
        };
    }
}
