using System.Security.Claims;
using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AuthenticateUser;
using Axis.Identity.Application.Queries.GetUserTokenClaims;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Endpoints;

public static class ConnectEndpoints
{
    public static IEndpointRouteBuilder MapConnectEndpoints(this IEndpointRouteBuilder app)
    {
        // Authorization Code + PKCE: browser redirects here to start the flow.
        // OpenIddict intercepts this URI — passthrough is enabled so we handle it.
        app.MapGet("/connect/authorize", (Delegate)Authorize)
            .WithName("Authorize")
            .WithSummary("Start Authorization Code + PKCE flow")
            .WithTags("OpenIddict")
            .ExcludeFromDescription();

        // Login form (API-style) — validates credentials and signs the user in via cookie.
        // Returns a redirect back to /connect/authorize to complete code issuance.
        app.MapPost("/connect/login", Login)
            .WithName("Login")
            .WithSummary("Validate credentials and issue session cookie for PKCE flow")
            .WithTags("OpenIddict")
            .RequireRateLimiting("auth")
            .DisableAntiforgery()
            .ExcludeFromDescription();

        // Token endpoint — OpenIddict intercepts; our handler builds the principal.
        // Handles: authorization_code, refresh_token, client_credentials.
        app.MapPost("/connect/token", Token)
            .WithName("Token")
            .WithSummary("Exchange code or refresh token for access token")
            .WithTags("OpenIddict")
            .RequireRateLimiting("auth")
            .DisableAntiforgery()
            .ExcludeFromDescription();

        return app;
    }

    // ── GET /connect/authorize ────────────────────────────────────────────────
    private static async Task<IResult> Authorize(HttpContext httpContext)
    {
        OpenIddictRequest? request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict server request not found.");

        // Check whether the user is already authenticated via the session cookie
        AuthenticateResult cookieResult = await httpContext.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (!cookieResult.Succeeded)
        {
            // Not authenticated — redirect to the login endpoint, passing the current URL
            // as a return_url so the user lands back here after a successful login
            string returnUrl = httpContext.Request.Path + httpContext.Request.QueryString;
            return Results.Redirect("/connect/login?return_url=" + Uri.EscapeDataString(returnUrl));
        }

        // Build the OpenIddict principal from the cookie session claims
        ClaimsPrincipal cookiePrincipal = cookieResult.Principal!;
        ClaimsPrincipal openIddictPrincipal = BuildOpenIddictPrincipal(
            cookiePrincipal, request.GetScopes());

        return Results.SignIn(
            openIddictPrincipal,
            properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // ── POST /connect/login ───────────────────────────────────────────────────
    private static async Task<IResult> Login(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm(Name = "return_url")] string? returnUrl,
        ISender mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result<AuthenticationResult> result = await mediator.Send(
            new AuthenticateUserCommand(email, password), ct);

        if (result.IsFailure || !result.Value.Success)
        {
            AuthFailureReason? reason = result.IsSuccess ? result.Value.FailureReason : null;
            string detail = reason switch
            {
                AuthFailureReason.AccountLocked =>
                    $"Too many failed attempts. Try again after {result.Value.LockedUntil:HH:mm} UTC.",
                AuthFailureReason.AccountDeactivated =>
                    "Your account has been deactivated. Contact your organization admin.",
                AuthFailureReason.EmailNotVerified =>
                    "Please verify your email before signing in.",
                AuthFailureReason.OrganizationDeleted =>
                    "This organization no longer exists.",
                _ => "Incorrect email or password.",
            };
            return Results.Problem(detail: detail, statusCode: StatusCodes.Status401Unauthorized);
        }

        AuthenticationResult auth = result.Value;

        // Issue a short-lived session cookie so the authorize endpoint can identify the user.
        // The cookie is not a session token — it only lives long enough to complete the PKCE
        // code exchange (5 minutes).
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, auth.UserId.ToString()),
            new(ClaimTypes.Email, auth.Email),
            new("name", auth.FullName),
        ];
        if (auth.OrganizationId is Guid organizationId)
            claims.Add(new Claim("org_id", organizationId.ToString()));
        foreach (string permission in auth.Permissions)
            claims.Add(new Claim("permissions", permission));

        ClaimsIdentity identity = new(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new(identity);

        AuthenticationProperties props = new()
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

        return Results.Redirect(returnUrl ?? "/");
    }

    // ── POST /connect/token ───────────────────────────────────────────────────
    private static async Task<IResult> Token(
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict server request not found.");

        // ── Authorization Code or Refresh Token ────────────────────────────
        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            AuthenticateResult result = await httpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            if (!result.Succeeded)
            {
                return Results.Challenge(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The token is no longer valid.",
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            string? sub = result.Principal!.GetClaim(Claims.Subject);
            if (!Guid.TryParse(sub, out Guid userId))
            {
                return Results.Challenge(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The token subject is invalid.",
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            string? orgIdStr = result.Principal!.GetClaim("org_id");
            Guid? orgId = Guid.TryParse(orgIdStr, out Guid parsedOrgId) ? parsedOrgId : null;

            Result<UserTokenClaimsDto> claimsResult = await mediator.Send(
                new GetUserTokenClaimsQuery(userId, orgId),
                ct);

            if (claimsResult.IsFailure)
            {
                return Results.Challenge(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            claimsResult.Error,
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            UserTokenClaimsDto claims = claimsResult.Value;
            ClaimsPrincipal principal = BuildUserPrincipal(
                claims.UserId,
                claims.OrganizationId,
                claims.Email,
                claims.FullName,
                claims.Permissions.ToList(),
                result.Principal!.GetScopes());

            return Results.SignIn(
                principal,
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // ── Client Credentials (M2M) ───────────────────────────────────────
        if (request.IsClientCredentialsGrantType())
        {
            ClaimsIdentity identity = new(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(Claims.Subject, request.ClientId!));
            identity.AddClaim(new Claim(Claims.ClientId, request.ClientId!));

            ClaimsPrincipal principal = new(identity);
            principal.SetScopes(request.GetScopes());
            ClaimDestinationsHelper.SetDestinations(principal);

            return Results.SignIn(
                principal,
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Results.Problem(
            detail: "The specified grant type is not supported.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClaimsPrincipal BuildOpenIddictPrincipal(
        ClaimsPrincipal cookiePrincipal,
        IEnumerable<string> scopes)
    {
        string sub = cookiePrincipal.FindFirstValue(ClaimTypes.NameIdentifier)!;
        string? orgId = cookiePrincipal.FindFirstValue("org_id");
        string? email = cookiePrincipal.FindFirstValue(ClaimTypes.Email);
        string? name = cookiePrincipal.FindFirstValue("name");
        IEnumerable<string> permissions = cookiePrincipal.FindAll("permissions").Select(c => c.Value);

        return BuildUserPrincipal(
            Guid.Parse(sub),
            Guid.TryParse(orgId, out Guid gOrgId) ? gOrgId : null,
            email ?? string.Empty,
            name ?? string.Empty,
            permissions.ToList(),
            scopes);
    }

    private static ClaimsPrincipal BuildUserPrincipal(
        Guid userId,
        Guid? orgId,
        string email,
        string name,
        IReadOnlyList<string> permissions,
        IEnumerable<string> scopes)
    {
        ClaimsIdentity identity = new(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(Claims.Subject, userId.ToString()));
        identity.AddClaim(new Claim(Claims.Email, email));
        identity.AddClaim(new Claim("name", name));
        if (orgId is Guid organizationId)
            identity.AddClaim(new Claim("org_id", organizationId.ToString()));

        foreach (string permission in permissions)
            identity.AddClaim(new Claim("permissions", permission));

        ClaimsPrincipal principal = new(identity);
        principal.SetScopes(scopes);
        ClaimDestinationsHelper.SetDestinations(principal);

        return principal;
    }
}
