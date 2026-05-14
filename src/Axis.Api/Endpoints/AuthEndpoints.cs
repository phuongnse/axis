using System.Security.Claims;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.RequestPasswordReset;
using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace Axis.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth");

        group.MapPost("/signout", SignOut)
            .RequireAuthorization()
            .WithName("SignOut")
            .WithSummary("Revoke the current refresh token and blacklist the access token")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401);

        group.MapPost("/verify-email", VerifyEmail)
            .WithName("VerifyEmail")
            .WithSummary("Verify email address with a one-time token")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400);

        group.MapPost("/resend-verification", ResendVerification)
            .WithName("ResendEmailVerification")
            .WithSummary("Resend email verification link")
            .WithTags("Identity")
            .Produces(204);

        group.MapPost("/forgot-password", ForgotPassword)
            .WithName("ForgotPassword")
            .WithSummary("Request a password reset link")
            .WithTags("Identity")
            .Produces<object>();

        group.MapPost("/reset-password", ResetPassword)
            .WithName("ResetPassword")
            .WithSummary("Reset password using a one-time token")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400);

        return app;
    }

    private static async Task<IResult> SignOut(
        CurrentUser currentUser,
        IJtiBlacklist jtiBlacklist,
        IOpenIddictTokenManager tokenManager,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Revoke the refresh token stored in the httpOnly cookie
        string? rawRefreshToken = httpContext.Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(rawRefreshToken))
        {
            object? token = await tokenManager.FindByReferenceIdAsync(rawRefreshToken, ct);
            if (token is not null)
                await tokenManager.TryRevokeAsync(token, ct);
        }

        // Blacklist the access token's JTI for its remaining lifetime so replayed
        // access tokens are rejected even before they naturally expire
        string? expStr = httpContext.User.FindFirst("exp")?.Value;
        TimeSpan ttl = TimeSpan.FromMinutes(15); // safe default
        if (expStr is not null && long.TryParse(expStr, out long expUnix))
        {
            TimeSpan remaining = DateTimeOffset.FromUnixTimeSeconds(expUnix) - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
                ttl = remaining;
        }
        await jtiBlacklist.BlacklistAsync(currentUser.Jti, ttl, ct);

        // Clear the refresh token cookie
        httpContext.Response.Cookies.Delete("refresh_token",
            new CookieOptions { Path = "/connect" });

        // Clear the PKCE session cookie (if still alive)
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.NoContent();
    }

    private static async Task<IResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new VerifyEmailCommand(request.Token), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ResendVerificationEmailCommand(request.Email), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Results.Ok(new
        {
            message = "If this email is registered, you'll receive a reset link shortly.",
        });
    }

    private static async Task<IResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ResetPasswordCommand(request.Token, request.NewPassword, request.ConfirmPassword), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}

public record VerifyEmailRequest(string Token);
public record ResendVerificationRequest(string Email);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
