using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AuthenticateUser;
using Axis.Identity.Application.Commands.RequestPasswordReset;
using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth");

        group.MapPost("/signin", SignIn)
            .WithName("SignIn")
            .WithSummary("Authenticate with email and password")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(401);

        group.MapPost("/refresh", Refresh)
            .WithName("RefreshToken")
            .WithSummary("Refresh access token using HttpOnly refresh cookie")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(401);

        group.MapPost("/signout", SignOut)
            .RequireAuthorization()
            .WithName("SignOut")
            .WithSummary("Sign out and revoke the current refresh token")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401);

        group.MapPost("/verify-email", VerifyEmail)
            .WithName("VerifyEmail")
            .WithSummary("Verify email address with a one-time token")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(422);

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
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> SignIn(
        [FromBody] SignInRequest request,
        ISender mediator,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result<AuthenticationResult> result = await mediator.Send(
            new AuthenticateUserCommand(request.Email, request.Password), ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        if (!result.Value.Success)
        {
            return result.Value.FailureReason switch
            {
                AuthFailureReason.AccountLocked => Results.Problem(
                    detail: $"Too many failed attempts. Try again after {result.Value.LockedUntil:HH:mm} UTC.",
                    statusCode: StatusCodes.Status401Unauthorized),
                AuthFailureReason.AccountDeactivated => Results.Problem(
                    detail: "Your account has been deactivated. Contact your organization admin.",
                    statusCode: StatusCodes.Status401Unauthorized),
                AuthFailureReason.EmailNotVerified => Results.Problem(
                    detail: "Please verify your email before signing in.",
                    statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.Problem(
                    detail: "Incorrect email or password.",
                    statusCode: StatusCodes.Status401Unauthorized),
            };
        }

        return await TokenHelper.IssueTokensAsync(
            result.Value.UserId, result.Value.OrganizationId,
            result.Value.Email, result.Value.FullName,
            result.Value.Permissions,
            tokenService, refreshTokenStore, configuration, httpContext, ct);
    }

    private static async Task<IResult> Refresh(
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? rawToken = httpContext.Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(rawToken))
            return Results.Problem("Missing refresh token.", statusCode: StatusCodes.Status401Unauthorized);

        string hash = tokenService.HashToken(rawToken);
        RefreshTokenInfo? info = await refreshTokenStore.FindByHashAsync(hash, ct);

        if (info is null)
        {
            httpContext.Response.Cookies.Delete("refresh_token",
                new CookieOptions { Path = "/api/auth" });
            return Results.Problem("The refresh token is invalid or has been revoked.", statusCode: StatusCodes.Status401Unauthorized);
        }

        User? user = await userRepository.GetByIdPlatformWideAsync(info.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            await refreshTokenStore.RevokeAsync(info.Id, ct);
            httpContext.Response.Cookies.Delete("refresh_token",
                new CookieOptions { Path = "/api/auth" });
            return Results.Problem("Account has been deactivated.", statusCode: StatusCodes.Status401Unauthorized);
        }

        IReadOnlyList<Role> roles = await roleRepository.GetByIdsAsync(user.RoleIds, info.OrganizationId!, ct);
        List<string> permissions = roles.SelectMany(r => r.Permissions).Distinct().ToList();

        // Revoke the used token before issuing a new one (refresh token rotation).
        // If the old token is replayed after this point it will be rejected, invalidating
        // all sessions for the user (detect-and-destroy pattern).
        await refreshTokenStore.RevokeAsync(info.Id, ct);
        httpContext.Response.Cookies.Delete("refresh_token",
            new CookieOptions { Path = "/api/auth" });

        return await TokenHelper.IssueTokensAsync(
            user.Id, info.OrganizationId,
            user.Email.Value, $"{user.FirstName} {user.LastName}",
            permissions,
            tokenService, refreshTokenStore, configuration, httpContext, ct);
    }

    private static async Task<IResult> SignOut(
        CurrentUser currentUser,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IJtiBlacklist jtiBlacklist,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? rawToken = httpContext.Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(rawToken))
        {
            string hash = tokenService.HashToken(rawToken);
            RefreshTokenInfo? info = await refreshTokenStore.FindByHashAsync(hash, ct);
            if (info is not null)
                await refreshTokenStore.RevokeAsync(info.Id, ct);
        }

        // Blacklist the access token's JTI for its remaining lifetime so it can't be
        // replayed after sign-out. TTL mirrors the token TTL — once the token would have
        // expired anyway the blacklist entry can safely be evicted.
        TimeSpan ttl = TimeSpan.FromMinutes(
            int.Parse(configuration["Jwt:AccessTokenTtlMinutes"] ?? "15"));
        await jtiBlacklist.BlacklistAsync(currentUser.Jti, ttl, ct);

        httpContext.Response.Cookies.Delete("refresh_token",
            new CookieOptions { Path = "/api/auth" });
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

public record SignInRequest(string Email, string Password);
public record VerifyEmailRequest(string Token);
public record ResendVerificationRequest(string Email);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
