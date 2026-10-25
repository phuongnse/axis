using Axis.Core.Tenancy;
using Axis.Data.Audit;
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

    /// <summary>The request item that <c>OnTokenValidated</c> sets when it rejects the ID token.</summary>
    public const string TokenRejectedItem = "axis:sign_in_token_rejected";

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
internal sealed partial class TenantSignInOptions(TenantSignInSettings settings, CookieSecurePolicy securePolicy, bool allowHttp)
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
                context.HttpContext.Items[TenantSignIn.TokenRejectedItem] = true;
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
        // A failure here fails the callback, so a sign-in never leaves a session without its record.
        options.Events.OnTicketReceived = async context =>
        {
            var user = Session.Read(context.Principal!)
                ?? throw new InvalidOperationException("The sign-in ticket holds no session user.");
            // The cookie handler would renew the session that the request's cookie names. This flag makes the
            // session store refuse that session, so every sign-in gets a new session id.
            context.HttpContext.Items[Session.SigningInItem] = true;
            await SessionAudit.AppendAsync(
                context.HttpContext,
                AuditActions.SessionSignedIn,
                user.Id,
                user.DisplayName,
                reason: null,
                context.HttpContext.RequestAborted);
        };
        // The response stays the handler's 500. The record is best effort, as the callback has already failed.
        options.Events.OnRemoteFailure = async context =>
        {
            var httpContext = context.HttpContext;
            try
            {
                var query = httpContext.Request.Query;
                var stateRead = context.Properties is not null;
                var otherTenantReadsState = !stateRead
                    && query["state"].ToString() is { Length: > 0 } state
                    && OtherTenantReadsState(httpContext, tenantId, state);
                var providerError = !string.IsNullOrEmpty(query["error"].ToString());
                var tokenRejected = httpContext.Items.ContainsKey(TenantSignIn.TokenRejectedItem);
                var reason = SignInRefusals.Classify(stateRead, otherTenantReadsState, providerError, context.Failure, tokenRejected);
                await SessionAudit.AppendAsync(
                    httpContext,
                    AuditActions.SessionSignInRefused,
                    AuditActors.Anonymous,
                    actorName: null,
                    reason,
                    httpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                LogRefusalNotRecorded(httpContext.RequestServices.GetRequiredService<ILogger<TenantSignInOptions>>(), exception);
            }
        };
    }

    // Each tenant's client protects its state with its own key purpose, so only a sign-in that
    // another tenant started can pass this check.
    private bool OtherTenantReadsState(HttpContext httpContext, string tenantId, string state)
    {
        var monitor = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        foreach (var otherId in settings.Current.Keys)
        {
            if (otherId == tenantId)
            {
                continue;
            }

            try
            {
                if (monitor.Get(TenantSignIn.SchemeName(otherId)).StateDataFormat.Unprotect(state) is not null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // A scheme that cannot read the state, or cannot be built, is not the sender.
            }
        }

        return false;
    }

    [LoggerMessage(LogLevel.Warning, "The refused sign-in callback could not be written to the audit records.")]
    private static partial void LogRefusalNotRecorded(ILogger logger, Exception exception);
}
