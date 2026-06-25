using System.Security.Claims;
using Axis.Api.Authorization;
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

        // Token endpoint — OpenIddict intercepts; our handler builds the principal.
        // Handles the authorization code issued after email verification.
        app.MapPost("/connect/token", Token)
            .WithName("Token")
            .WithSummary("Exchange authorization code for access token")
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
            return Results.Unauthorized();
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

    // ── POST /connect/token ───────────────────────────────────────────────────
    private static async Task<IResult> Token(
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict server request not found.");

        if (request.IsAuthorizationCodeGrantType())
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

            string? WorkspaceIdStr = result.Principal!.GetClaim("workspace_id");
            Guid? workspaceId = Guid.TryParse(WorkspaceIdStr, out Guid parsedWorkspaceId) ? parsedWorkspaceId : null;

            Result<UserTokenClaimsDto> claimsResult = await mediator.Send(
                new GetUserTokenClaimsQuery(userId, workspaceId),
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
                claims.workspaceId,
                claims.Email,
                claims.FullName,
                result.Principal!.GetScopes());

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
        string? WorkspaceId = cookiePrincipal.FindFirstValue("workspace_id");
        string? email = cookiePrincipal.FindFirstValue(ClaimTypes.Email);
        string? name = cookiePrincipal.FindFirstValue("name");

        return BuildUserPrincipal(
            Guid.Parse(sub),
            Guid.TryParse(WorkspaceId, out Guid gWorkspaceId) ? gWorkspaceId : null,
            email ?? string.Empty,
            name ?? string.Empty,
            scopes);
    }

    private static ClaimsPrincipal BuildUserPrincipal(
        Guid userId,
        Guid? workspaceId,
        string email,
        string name,
        IEnumerable<string> scopes)
    {
        ClaimsIdentity identity = new(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(Claims.Subject, userId.ToString()));
        identity.AddClaim(new Claim(Claims.Email, email));
        identity.AddClaim(new Claim("name", name));
        if (workspaceId is Guid resolvedWorkspaceId)
            identity.AddClaim(new Claim("workspace_id", resolvedWorkspaceId.ToString()));

        ClaimsPrincipal principal = new(identity);
        principal.SetScopes(scopes);
        ClaimDestinationsHelper.SetDestinations(principal);

        return principal;
    }
}
