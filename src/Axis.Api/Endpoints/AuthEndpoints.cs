using System.Security.Claims;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.RequestPasswordReset;
using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Commands.RetryTenantProvisioning;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Queries.GetProvisioningStatus;
using Axis.Shared.Application;
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
            .AllowAnonymous()
            .WithName("VerifyEmail")
            .WithSummary("Verify an email token and return the next registration or sign-in step")
            .WithTags("Identity")
            .Produces<VerifyEmailSessionEstablishedDto>()
            .ProducesProblem(400);

        group.MapGet("/provisioning-status", GetProvisioningStatus)
            .AllowAnonymous()
            .WithName("GetProvisioningStatus")
            .WithSummary("Poll tenant provisioning progress after email verification")
            .WithTags("Identity")
            .Produces<ProvisioningStatusDto>()
            .Produces(404);

        group.MapPost("/retry-provisioning", RetryProvisioning)
            .AllowAnonymous()
            .WithName("RetryTenantProvisioning")
            .WithSummary("Manually re-queue tenant provisioning after automatic retries are exhausted")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400);

        group.MapPost("/resend-verification", ResendVerification)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("ResendEmailVerification")
            .WithSummary("Resend email verification link")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/forgot-password", ForgotPassword)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("ForgotPassword")
            .WithSummary("Request a password reset link")
            .WithTags("Identity")
            .Produces<MessageResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/reset-password", ResetPassword)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("ResetPassword")
            .WithSummary("Reset password using a one-time token")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

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
        List<Claim> claimList =
        [
            new(ClaimTypes.NameIdentifier, claims.UserId!.Value.ToString()),
            new(ClaimTypes.Email, claims.Email),
            new("name", claims.FullName),
        ];
        if (claims.tenantId is Guid tenantId)
            claimList.Add(new Claim("tenant_id", tenantId.ToString()));
        foreach (string permission in claims.Permissions)
            claimList.Add(new Claim("permissions", permission));

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

    private static async Task<IResult> GetProvisioningStatus(
        [FromQuery] string token,
        ISender mediator,
        CancellationToken ct)
    {
        ProvisioningStatusDto? status = await mediator.Send(new GetProvisioningStatusQuery(token), ct);
        if (status is null)
            return Results.NotFound();

        return Results.Ok(status);
    }

    private static async Task<IResult> RetryProvisioning(
        [FromBody] RetryProvisioningRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new RetryTenantProvisioningCommand(request.Token), ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.NoContent();
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

    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Results.Ok(new MessageResponse(
            "If this email is registered, you'll receive a reset link shortly."));
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
