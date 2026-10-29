using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Server.Http;
using Axis.Server.Users;
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
/// <c>POST /api/auth/sign-out</c> ends the session and answers the identity provider's sign-out
/// address, for the SPA to send the browser to. The answer carries <c>Clear-Site-Data</c>, so the
/// browser drops the site's cache and storage.
/// </summary>
internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");
        auth.MapGet("/sign-in", SignInAsync).WithRateLimit(Partition.SignIn);
        auth.MapPost("/sign-out", SignOutAsync);
    }

    // Only a local path is a return address, so sign-in can never send the browser to another site.
    private static async Task<IResult> SignInAsync(
        string? returnUrl,
        ITenantContextAccessor tenants,
        SiteSignIn sites,
        CancellationToken cancellationToken,
        bool reauthenticate = false)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = "/";
        }
        else if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The return address must be a local path.");
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
        }

        if (reauthenticate)
        {
            // The handler prefers these over its options, so the identity provider asks for credentials even when its own session is open.
            properties.SetParameter(OpenIdConnectParameterNames.Prompt, "login");
            properties.SetParameter<TimeSpan?>(OpenIdConnectParameterNames.MaxAge, TimeSpan.Zero);
        }

        return Results.Challenge(properties, [TenantSignIn.SchemeName(tenant.TenantId)]);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext httpContext,
        ITenantContextAccessor tenants,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        CancellationToken cancellationToken)
    {
        // A session of another tenant, or an expired one, counts as none and writes no record.
        if (await CurrentUsers.FindAsync(httpContext) is { } user)
        {
            await SessionAudit.AppendAsync(httpContext, AuditActions.SessionSignedOut, user.Id, user.DisplayName, reason: null, cancellationToken);
        }

        await httpContext.SignOutAsync(Session.Scheme);

        // The browser drops the site's HTTP cache and storage once it receives this answer (ASVS V14.3.1).
        httpContext.Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";

        var tenant = tenants.Current ?? throw new InvalidOperationException("No tenant context is set.");
        var options = oidcOptions.Get(TenantSignIn.SchemeName(tenant.TenantId));
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        var request = httpContext.Request;
        var returnUrl = $"{request.Scheme}://{request.Host}/";
        var signOutUrl = string.IsNullOrEmpty(configuration.EndSessionEndpoint)
            // An identity provider without a sign-out address leaves the browser on the site.
            ? "/"
            : $"{configuration.EndSessionEndpoint}?client_id={Uri.EscapeDataString(options.ClientId!)}&post_logout_redirect_uri={Uri.EscapeDataString(returnUrl)}";
        return Results.Ok(new SignOutResponse(signOutUrl));
    }

    private sealed record SignOutResponse(string SignOutUrl);
}
