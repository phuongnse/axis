using System.Text.RegularExpressions;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Server.Http;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Axis.Server.Authentication;

/// <summary>
/// Sign-in and sign-out through the tenant's identity provider. <c>GET /api/auth/sign-in</c>
/// redirects there. The identity provider returns to <c>GET /api/auth/callback</c>, which the
/// tenant's OIDC handler serves: it creates the <see cref="Session"/> and redirects to the
/// <c>returnUrl</c>. A callback that fails creates no session and redirects to
/// <see cref="TenantSignIn.FailedPath"/>. A <c>returnUrl</c> that opens an active site starts the sign-in through that site's
/// client, see <see cref="SiteSignIn"/>.
/// <c>POST /api/auth/sign-out</c> ends the session. It first ends the identity provider's own session by the stored <c>sid</c>, as
/// the realm's <c>axis-users</c> client, and answers <c>/?signedOut</c>. It answers the identity provider's sign-out address, for
/// the SPA to send the browser to, only when that call cannot be made or fails. The answer carries <c>Clear-Site-Data</c>, so the
/// browser drops the site's cache and storage.
/// </summary>
internal static partial class AuthEndpoints
{
    internal const string ActionParameter = "kc_action";

    private static readonly TimeSpan _identityProviderLimit = TimeSpan.FromSeconds(5);

    // The identity provider's application-initiated actions that the account page starts. Matching is exact and case-sensitive.
    [GeneratedRegex("^(UPDATE_PASSWORD|CONFIGURE_TOTP|delete_credential:[A-Za-z0-9-]{1,64})$")]
    private static partial Regex AllowedAction();

    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");
        auth.MapGet("/sign-in", SignInAsync).WithRateLimit(Partition.SignIn);
        auth.MapPost("/sign-out", SignOutAsync);
    }

    // Only a local path is a return address, so sign-in can never send the browser to another site.
    private static async Task<IResult> SignInAsync(
        HttpContext httpContext,
        string? returnUrl,
        ITenantContextAccessor tenants,
        SiteSignIn sites,
        CancellationToken cancellationToken,
        bool reauthenticate = false,
        string? action = null)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = "/";
        }
        else if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The return address must be a local path.");
        }

        if (action is not null && !AllowedAction().IsMatch(action))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The action is not one that sign-in allows.");
        }

        var tenant = tenants.Current ?? throw new InvalidOperationException("No tenant context is set.");
        var properties = new AuthenticationProperties { RedirectUri = returnUrl };
        if (await sites.FindAsync(tenant.TenantId, returnUrl, cancellationToken) is { } site)
        {
            properties.Items[SiteSignIn.SitePathItem] = site.Path;
            if (site.ProviderHint is { } hint)
            {
                // A parameter is not saved in the state, and only the redirect needs the hint.
                properties.SetParameter(SiteSignIn.ProviderHintParameter, hint);
            }

            if (site.AcrValues is { } acrValues)
            {
                properties.SetParameter(SiteSignIn.AcrValuesParameter, acrValues);
            }

            // The realm's own session would sign the person in again through the provider that this site refuses.
            var current = await httpContext.AuthenticateAsync(Session.Scheme);
            if (current.Succeeded && !site.Allows(Session.ReadIdentityProvider(current.Principal)))
            {
                properties.SetParameter(OpenIdConnectParameterNames.Prompt, "login");
            }
        }

        if (reauthenticate)
        {
            // The handler prefers these over its options, so the identity provider asks for credentials even when its own session is open.
            properties.SetParameter(OpenIdConnectParameterNames.Prompt, "login");
            properties.SetParameter<TimeSpan?>(OpenIdConnectParameterNames.MaxAge, TimeSpan.Zero);
        }

        if (action is not null)
        {
            // A parameter is not saved in the state, and only the redirect needs the action.
            properties.SetParameter(ActionParameter, action);
        }

        return Results.Challenge(properties, [TenantSignIn.SchemeName(tenant.TenantId)]);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext httpContext,
        ITenantContextAccessor tenants,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        ITenantCatalog catalog,
        IHttpClientFactory httpClients,
        ILogger<AuthLog> logger,
        CancellationToken cancellationToken)
    {
        // A session of another tenant, or an expired one, counts as none and writes no record.
        if (await CurrentUsers.FindAsync(httpContext) is { } user)
        {
            await SessionAudit.AppendAsync(httpContext, AuditActions.SessionSignedOut, user.Id, reason: null, cancellationToken);
        }

        // The session store reads the sid from the row, so it is read before the sign-out deletes the row.
        var current = await httpContext.AuthenticateAsync(Session.Scheme);
        var idpSessionId = current.Succeeded ? Session.ReadIdpSessionId(current.Principal) : null;

        await httpContext.SignOutAsync(Session.Scheme);

        // The browser drops the site's HTTP cache and storage once it receives this answer (ASVS V14.3.1).
        httpContext.Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";

        var tenant = tenants.Current ?? throw new InvalidOperationException("No tenant context is set.");
        if (idpSessionId is not null
            && catalog.Find(tenant.TenantId)?.RealmUsers is { } access
            && await TryEndIdentityProviderSessionAsync(httpClients, access, idpSessionId, tenant.TenantId, logger, cancellationToken))
        {
            return Results.Ok(new SignOutResponse("/?signedOut"));
        }

        var options = oidcOptions.Get(TenantSignIn.SchemeName(tenant.TenantId));
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        var request = httpContext.Request;
        var returnUrl = $"{request.Scheme}://{request.Host}/?signedOut";
        var signOutUrl = string.IsNullOrEmpty(configuration.EndSessionEndpoint)
            // An identity provider without a sign-out address leaves the browser on the signed-out card, because
            // "/" would start sign-in at once and its session is still open.
            ? "/?signedOut"
            : $"{configuration.EndSessionEndpoint}?client_id={Uri.EscapeDataString(options.ClientId!)}&post_logout_redirect_uri={Uri.EscapeDataString(returnUrl)}";
        return Results.Ok(new SignOutResponse(signOutUrl));
    }

    // A realm that no longer has the session counts as ended. The call gets a limit, so a slow identity provider cannot hold sign-out up.
    private static async Task<bool> TryEndIdentityProviderSessionAsync(
        IHttpClientFactory httpClients,
        TenantRealmUsers access,
        string idpSessionId,
        string tenantId,
        ILogger<AuthLog> logger,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_identityProviderLimit);
        try
        {
            using var client = httpClients.CreateClient(nameof(AuthEndpoints));
            await new RealmUsers(client, access).EndSessionAsync(idpSessionId, limit.Token);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogEndSessionFailed(logger, tenantId, exception.Message);
            return false;
        }
    }

    [LoggerMessage(LogLevel.Warning, "The identity provider session of a user of tenant {TenantId} could not be ended. {Reason}")]
    private static partial void LogEndSessionFailed(ILogger logger, string tenantId, string reason);

    private sealed record SignOutResponse(string SignOutUrl);
}

/// <summary>The category of the log lines of the auth routes.</summary>
internal sealed class AuthLog;
