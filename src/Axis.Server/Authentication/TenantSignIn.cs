using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Profiles;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
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

    /// <summary>
    /// Where the browser goes when the callback fails: the shell's sign-in page. The address holds no reason,
    /// as the reason is only in the audit record.
    /// </summary>
    public const string FailedPath = "/?signIn=failed";

    public const string SchemePrefix = "oidc-";

    /// <summary>
    /// The request item that <c>OnTokenValidated</c> sets when it rejects the ID token for a missing user id, or for an
    /// <c>auth_time</c> that is missing or older than the session's lifetime.
    /// </summary>
    public const string TokenRejectedItem = "axis:sign_in_token_rejected";

    /// <summary>
    /// The request item that <c>OnAuthorizationCodeReceived</c> sets when it rejects the provider metadata's issuer, or under
    /// <c>fapi2</c> the callback's <c>iss</c>.
    /// </summary>
    public const string IssuerRejectedItem = "axis:sign_in_issuer_rejected";

    /// <summary>
    /// The request item that <c>OnTokenValidated</c> sets when the tenant's platform sign-in does not accept the provider
    /// of the sign-in, or does not accept accounts.
    /// </summary>
    public const string ProviderRefusedItem = "axis:sign_in_provider_refused";

    /// <summary>The request item that <c>OnTokenValidated</c> fills with the <see cref="ProfileClaims"/> for <c>OnTicketReceived</c>.</summary>
    public const string ProfileClaimsItem = "axis:sign_in_profile";

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

/// <summary>The email and locale of a signed-in user from the ID token. A claim the token lacks is <c>null</c>.</summary>
internal sealed record ProfileClaims(string? Email, string? Locale);

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
        // A fapi2 tenant proves who it is with a signed assertion, so it sends no client secret even when one is set.
        var fapi2 = tenant.Profile == TenantSignInProfiles.Fapi2;
        options.ClientSecret = fapi2 ? null : tenant.ClientSecret?.Reveal();
        options.RequireHttpsMetadata = !allowHttp;
        options.ResponseType = "code";
        // The identity provider returns with a GET. A cross-site form post would not carry
        // the SameSite=Lax correlation and nonce cookies.
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        // The identity provider must ask a person whose login there is older than the session's absolute lifetime to log in again.
        options.MaxAge = Session.MaxLifetime;
        // Providers other than Keycloak put the email in the ID token only when this scope is requested.
        if (!options.Scope.Contains("email"))
        {
            options.Scope.Add("email");
        }

        // A standard tenant sends the authorization request, PKCE included, in the redirect (D9). A fapi2 tenant
        // pushes it (PAR, D83). The redirect event handles a provider without a PAR endpoint, so Require is a backstop.
        options.PushedAuthorizationBehavior = fapi2 ? PushedAuthorizationBehavior.Require : PushedAuthorizationBehavior.Disable;
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
        // Their path is the callback path, so the __Host- prefix, which needs path /, is not possible.
        if (securePolicy == CookieSecurePolicy.Always)
        {
            options.CorrelationCookie.Name = "__Secure-Axis.Correlation.";
            options.NonceCookie.Name = "__Secure-Axis.Nonce.";
        }

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

            // MapInboundClaims is false, so the claim keeps the name that the token gives it.
            var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            var authTime = principal.FindFirst("auth_time")?.Value;
            if (!SignInRecency.IsRecent(authTime, now, context.Options.TokenValidationParameters.ClockSkew))
            {
                context.HttpContext.Items[TenantSignIn.TokenRejectedItem] = true;
                context.Fail("The ID token's auth_time is missing or older than the session's lifetime.");
                return Task.CompletedTask;
            }

            var displayName = principal.FindFirst(tenant.Claims.DisplayName)?.Value;
            var roles = principal.FindAll(tenant.Claims.Roles).Select(claim => claim.Value);
            if (tenant.PlatformSignIn is { } platform)
            {
                // The roles the tenant's roles claim gives stay, and the roles that the provider's mapping gives are added.
                var services = context.HttpContext.RequestServices;
                var mapped = PlatformRoles.Resolve(
                    platform,
                    principal.FindFirst(RealmSync.IdentityProviderClaim)?.Value,
                    name => principal.FindAll(name).Select(claim => claim.Value),
                    services.GetRequiredService<StudioOptions>(),
                    services.GetRequiredService<ConsoleSettings>(),
                    services.GetRequiredService<UsersSettings>());
                if (mapped is null)
                {
                    context.HttpContext.Items[TenantSignIn.ProviderRefusedItem] = true;
                    context.Fail("The platform sign-in does not accept the provider.");
                    return Task.CompletedTask;
                }

                roles = roles.Concat(mapped).Distinct(StringComparer.Ordinal);
            }

            // The email and locale are not session claims. They go to OnTicketReceived in a request item.
            context.HttpContext.Items[TenantSignIn.ProfileClaimsItem] = new ProfileClaims(
                Blank(principal.FindFirst(tenant.Claims.Email)?.Value),
                Blank(principal.FindFirst(tenant.Claims.Locale)?.Value));
            context.Principal = Session.CreatePrincipal(
                tenantId,
                userId,
                string.IsNullOrWhiteSpace(displayName) ? userId : displayName,
                roles,
                authenticatedAt: SignInRecency.TryRead(authTime, out var authenticatedAt) ? authenticatedAt : null);
            return Task.CompletedTask;
        };
        // Every profile refuses metadata whose issuer is not the tenant's authority, before the code goes to the token endpoint.
        options.Events.OnAuthorizationCodeReceived = async context => await CheckMetadataIssuerAsync(context, tenant);
        if (fapi2)
        {
            ConfigureFapi2(options, tenant, tenantId);
        }

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
            await SaveProfileAsync(context.HttpContext, user);
        };
        // The browser goes to the shell's sign-in page. The record is best effort, as the callback has already failed.
        options.Events.OnRemoteFailure = async context =>
        {
            var httpContext = context.HttpContext;
            try
            {
                var query = httpContext.Request.Query;
                var tokenRejected = httpContext.Items.ContainsKey(TenantSignIn.TokenRejectedItem);
                var issuerRejected = httpContext.Items.ContainsKey(TenantSignIn.IssuerRejectedItem);
                // A rejected token, issuer or provider comes after the state was read, but the failure it leaves carries no properties.
                var providerRefused = httpContext.Items.ContainsKey(TenantSignIn.ProviderRefusedItem);
                var stateRead = context.Properties is not null || tokenRejected || issuerRejected || providerRefused;
                var otherTenantReadsState = !stateRead
                    && query["state"].ToString() is { Length: > 0 } state
                    && OtherTenantReadsState(httpContext, tenantId, state);
                var providerError = !string.IsNullOrEmpty(query["error"].ToString());
                var reason = SignInRefusals.Classify(stateRead, otherTenantReadsState, providerError, context.Failure, tokenRejected, issuerRejected, providerRefused);
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

            context.Response.Redirect(TenantSignIn.FailedPath);
            context.HandleResponse();
        };
    }

    // The handler checks that the ID token's iss matches the metadata, so metadata from another issuer would pass with a
    // token from that issuer. This check compares the metadata's issuer with the tenant's authority. It returns the
    // metadata, or null after it failed the callback.
    private static async Task<OpenIdConnectConfiguration?> CheckMetadataIssuerAsync(
        AuthorizationCodeReceivedContext context,
        TenantOidcSettings tenant)
    {
        var httpContext = context.HttpContext;
        var configuration = await context.Options.ConfigurationManager!.GetConfigurationAsync(httpContext.RequestAborted);
        if (!string.Equals(configuration.Issuer, tenant.Authority!.TrimEnd('/'), StringComparison.Ordinal))
        {
            httpContext.Items[TenantSignIn.IssuerRejectedItem] = true;
            context.Fail("The provider metadata's issuer is not the tenant's authority.");
            return null;
        }

        return configuration;
    }

    // Under fapi2 the client authenticates with private_key_jwt at the PAR and token endpoints, signed with the newest
    // client key. The identity provider refuses a reused jti, so each request gets its own assertion. The callback must
    // carry an iss that is the tenant's authority (RFC 9207), and the token request carries a DPoP proof (RFC 9449).
    // Both run in the code event after the metadata check, so a refused issuer never sends the code to the token endpoint.
    private static void ConfigureFapi2(OpenIdConnectOptions options, TenantOidcSettings tenant, string tenantId)
    {
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            var configuration = await context.Options.ConfigurationManager!.GetConfigurationAsync(context.HttpContext.RequestAborted);
            if (string.IsNullOrEmpty(configuration.PushedAuthorizationRequestEndpoint))
            {
                LogNoPushedAuthorization(context.HttpContext.RequestServices.GetRequiredService<ILogger<TenantSignInOptions>>(), tenantId);
                context.Response.Redirect(TenantSignIn.FailedPath);
                context.HandleResponse();
            }
        };
        options.Events.OnPushAuthorization = context =>
        {
            Authenticate(context.HttpContext, context.ProtocolMessage, tenant);
            context.HandleClientAuthentication();
            return Task.CompletedTask;
        };
        options.Events.OnAuthorizationCodeReceived = async context =>
        {
            var httpContext = context.HttpContext;
            if (await CheckMetadataIssuerAsync(context, tenant) is not { } configuration)
            {
                return;
            }

            var issuer = context.ProtocolMessage.GetParameter(OpenIdConnectParameterNames.Iss);
            if (!string.Equals(issuer, tenant.Authority!.TrimEnd('/'), StringComparison.Ordinal))
            {
                httpContext.Items[TenantSignIn.IssuerRejectedItem] = true;
                context.Fail("The authorization response's iss is not the tenant's authority.");
                return;
            }

            var response = await DpopTokenRequest.SendAsync(
                context.Backchannel,
                configuration.TokenEndpoint,
                () =>
                {
                    Authenticate(httpContext, context.TokenEndpointRequest!, tenant);
                    return context.TokenEndpointRequest!;
                },
                httpContext.RequestServices.GetRequiredService<TimeProvider>(),
                httpContext.RequestAborted);
            context.HandleCodeRedemption(response);
        };
    }

    private static void Authenticate(HttpContext httpContext, OpenIdConnectMessage message, TenantOidcSettings tenant)
    {
        var clock = httpContext.RequestServices.GetRequiredService<TimeProvider>();
        message.ClientAssertionType = ClientAssertion.Type;
        message.ClientAssertion = ClientAssertion.Create(tenant.ClientId!, tenant.Authority!, tenant.ClientKeys[0], clock.GetUtcNow());
    }

    // A profile that cannot be written never refuses the sign-in. The log holds no email or locale.
    private static async Task SaveProfileAsync(HttpContext httpContext, CurrentUserResponse user)
    {
        try
        {
            var claims = httpContext.Items[TenantSignIn.ProfileClaimsItem] as ProfileClaims;
            var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>()
                .GetConnectionAsync(httpContext.RequestAborted);
            await UserProfiles.UpsertAsync(
                connection,
                user.Id,
                user.DisplayName,
                claims?.Email,
                claims?.Locale,
                httpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            LogProfileNotWritten(httpContext.RequestServices.GetRequiredService<ILogger<TenantSignInOptions>>(), exception);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

    [LoggerMessage(LogLevel.Warning, "Sign-in of tenant {TenantId} failed: its fapi2 profile needs pushed authorization requests, and the identity provider offers no such endpoint.")]
    private static partial void LogNoPushedAuthorization(ILogger logger, string tenantId);

    [LoggerMessage(LogLevel.Warning, "The profile of the signed-in user could not be written.")]
    private static partial void LogProfileNotWritten(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The refused sign-in callback could not be written to the audit records.")]
    private static partial void LogRefusalNotRecorded(ILogger logger, Exception exception);
}
