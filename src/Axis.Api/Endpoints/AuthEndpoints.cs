using System.Security.Claims;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.ResendVerificationEmail;
using Axis.Identity.Application.Commands.SignInUser;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth");

        group.MapGet("/session", GetSession)
            .AllowAnonymous()
            .WithName("GetBrowserSession")
            .WithSummary("Resolve the current same-origin browser session")
            .WithTags("Identity")
            .Produces<AxisBrowserSessionDto>();

        group.MapPost("/sign-in", SignIn)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("SignInUser")
            .WithSummary("Sign in a standalone user account")
            .WithTags("Identity")
            .Produces<SignInSessionEstablishedDto>()
            .ProducesProblem(400)
            .ProducesProblem(422)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/verify-email", VerifyEmail)
            .AllowAnonymous()
            .WithName("VerifyEmail")
            .WithSummary("Verify an email token and establish the registration session")
            .WithTags("Identity")
            .Produces<VerifyEmailSessionEstablishedDto>()
            .ProducesProblem(400);

        group.MapPost("/resend-verification", ResendVerification)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("ResendEmailVerification")
            .WithSummary("Resend email verification link")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/sign-out", (Delegate)SignOut)
            .AllowAnonymous()
            .WithName("SignOutUser")
            .WithSummary("Sign out the current browser session")
            .WithTags("Identity")
            .Produces(204);

        return app;
    }

    private static async Task<IResult> SignIn(
        [FromBody] SignInUserRequest request,
        ISender mediator,
        AxisBrowserSessionPolicy sessionPolicy,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result<SignInSuccessDto> result =
            await mediator.Send(new SignInUserCommand(request.Email, request.Password), ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        await SignInBrowserSessionAsync(httpContext, sessionPolicy, result.Value);

        return Results.Ok(SignInSessionEstablishedDto.From(result.Value));
    }

    private static async Task<IResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        ISender mediator,
        AxisBrowserSessionPolicy sessionPolicy,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result<VerifyEmailSuccessDto> result =
            await mediator.Send(new VerifyEmailCommand(request.Token), ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        if (result.Value.SessionEstablished)
            await SignInBrowserSessionAsync(httpContext, sessionPolicy, result.Value);

        return Results.Ok(VerifyEmailSessionEstablishedDto.From(result.Value));
    }

    private static async Task SignInBrowserSessionAsync(
        HttpContext httpContext,
        AxisBrowserSessionPolicy sessionPolicy,
        SignInSuccessDto claims)
    {
        await SignInBrowserSessionAsync(
            httpContext,
            sessionPolicy,
            claims.UserId,
            claims.workspaceId,
            claims.Email,
            claims.FullName);
    }

    private static async Task SignInBrowserSessionAsync(
        HttpContext httpContext,
        AxisBrowserSessionPolicy sessionPolicy,
        VerifyEmailSuccessDto claims)
    {
        await SignInBrowserSessionAsync(
            httpContext,
            sessionPolicy,
            claims.UserId!.Value,
            claims.workspaceId,
            claims.Email,
            claims.FullName);
    }

    private static async Task SignInBrowserSessionAsync(
        HttpContext httpContext,
        AxisBrowserSessionPolicy sessionPolicy,
        Guid userId,
        Guid? workspaceId,
        string email,
        string fullName)
    {
        List<Claim> claimList =
        [
            new(Claims.Subject, userId.ToString()),
            new(Claims.Email, email),
            new(Claims.Name, fullName),
        ];
        if (workspaceId is Guid resolvedWorkspaceId)
            claimList.Add(new Claim("workspace_id", resolvedWorkspaceId.ToString()));

        ClaimsIdentity identity = new(claimList, CookieAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            sessionPolicy.CreateAuthenticationProperties());
    }

    private static async Task<IResult> GetSession(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(httpContext);
        string requestToken = tokens.RequestToken
            ?? throw new InvalidOperationException("The antiforgery request token was not generated.");
        AuthenticateResult result = await httpContext.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
            return Results.Ok(new AxisBrowserSessionDto(false, requestToken, null));

        ClaimsPrincipal principal = result.Principal;
        string? subject = principal.GetClaim(Claims.Subject);
        string? email = principal.GetClaim(Claims.Email);
        string? name = principal.GetClaim(Claims.Name);
        if (!Guid.TryParse(subject, out Guid userId) ||
            string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(name))
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new AxisBrowserSessionDto(false, requestToken, null));
        }

        Guid? workspaceId = Guid.TryParse(
            principal.FindFirstValue("workspace_id"),
            out Guid parsedWorkspaceId)
            ? parsedWorkspaceId
            : null;
        return Results.Ok(new AxisBrowserSessionDto(
            true,
            requestToken,
            new AxisBrowserSessionUserDto(userId, workspaceId, email, name)));
    }

    private static async Task<IResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ResendVerificationEmailCommand(request.Email), ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.NoContent();
    }

    private static async Task<IResult> SignOut(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }
}

public sealed record AxisBrowserSessionDto(
    bool Authenticated,
    string CsrfToken,
    AxisBrowserSessionUserDto? User);

public sealed record AxisBrowserSessionUserDto(
    Guid UserId,
    Guid? WorkspaceId,
    string Email,
    string Name);
