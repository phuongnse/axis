using System.Security.Claims;
using Axis.Api.Extensions;
using Axis.Identity.Application.Commands.ResendVerificationEmail;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth");

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

        return app;
    }

    private static async Task<IResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        ISender mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result<VerifyEmailSuccessDto> result =
            await mediator.Send(new VerifyEmailCommand(request.Token), ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        if (result.Value.SessionEstablished)
            await SignInPkceSessionAsync(httpContext, result.Value);

        return Results.Ok(VerifyEmailSessionEstablishedDto.From(result.Value));
    }

    private static async Task SignInPkceSessionAsync(HttpContext httpContext, VerifyEmailSuccessDto claims)
    {
        await SignInPkceSessionAsync(
            httpContext,
            claims.UserId!.Value,
            claims.workspaceId,
            claims.Email,
            claims.FullName);
    }

    private static async Task SignInPkceSessionAsync(
        HttpContext httpContext,
        Guid userId,
        Guid? workspaceId,
        string email,
        string fullName)
    {
        List<Claim> claimList =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new("name", fullName),
        ];
        if (workspaceId is Guid resolvedWorkspaceId)
            claimList.Add(new Claim("workspace_id", resolvedWorkspaceId.ToString()));

        ClaimsIdentity identity = new(claimList, CookieAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new(identity);

        AuthenticationProperties props = new()
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            props);
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
}
