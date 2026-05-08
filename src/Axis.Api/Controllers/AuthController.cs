using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AuthenticateUser;
using Axis.Identity.Application.Commands.RequestPasswordReset;
using Axis.Identity.Application.Commands.ResetPassword;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    ISender mediator,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokenStore,
    IJtiBlacklist jtiBlacklist,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IConfiguration configuration) : ControllerBase
{
    // POST /api/auth/signin
    [HttpPost("signin")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new AuthenticateUserCommand(request.Email, request.Password), ct);

        if (!result.Success)
        {
            return result.FailureReason switch
            {
                AuthFailureReason.AccountLocked => Unauthorized(new
                {
                    error = "account_locked",
                    message = $"Too many failed attempts. Try again after {result.LockedUntil:HH:mm} UTC.",
                    locked_until = result.LockedUntil,
                }),
                AuthFailureReason.AccountDeactivated => Unauthorized(new
                {
                    error = "account_deactivated",
                    message = "Your account has been deactivated. Contact your organization admin.",
                }),
                AuthFailureReason.EmailNotVerified => Unauthorized(new
                {
                    error = "email_not_verified",
                    message = "Please verify your email before signing in.",
                }),
                _ => Unauthorized(new
                {
                    error = "invalid_credentials",
                    message = "Incorrect email or password.",
                }),
            };
        }

        return await IssueTokensAsync(
            result.UserId, result.OrganizationId,
            result.Email, result.FullName,
            result.Permissions,
            Request.Headers.UserAgent.ToString(), ct);
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var rawToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(rawToken))
            return Unauthorized(new { error = "missing_refresh_token" });

        var hash = tokenService.HashToken(rawToken);
        var info = await refreshTokenStore.FindByHashAsync(hash, ct);

        if (info is null)
        {
            Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/auth" });
            return Unauthorized(new { error = "invalid_refresh_token" });
        }

        // Load fresh user data + permissions
        var user = await userRepository.GetByIdPlatformWideAsync(info.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            await refreshTokenStore.RevokeAsync(info.Id, ct);
            Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/auth" });
            return Unauthorized(new { error = "account_deactivated" });
        }

        var roles = await roleRepository.GetByIdsAsync(user.RoleIds, info.OrganizationId!, ct);
        var permissions = roles.SelectMany(r => r.Permissions).Distinct().ToList();

        // Rotate: revoke old, issue new
        await refreshTokenStore.RevokeAsync(info.Id, ct);
        Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/auth" });

        return await IssueTokensAsync(
            user.Id, info.OrganizationId,
            user.Email.Value, $"{user.FirstName} {user.LastName}",
            permissions,
            Request.Headers.UserAgent.ToString(), ct);
    }

    // POST /api/auth/signout
    [Authorize]
    [HttpPost("signout")]
    public async Task<IActionResult> SignOut([FromServices] CurrentUser currentUser, CancellationToken ct)
    {
        var rawToken = Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(rawToken))
        {
            var hash = tokenService.HashToken(rawToken);
            var info = await refreshTokenStore.FindByHashAsync(hash, ct);
            if (info is not null)
                await refreshTokenStore.RevokeAsync(info.Id, ct);
        }

        var ttl = TimeSpan.FromMinutes(
            int.Parse(configuration["Jwt:AccessTokenTtlMinutes"] ?? "15"));
        await jtiBlacklist.BlacklistAsync(currentUser.Jti, ttl, ct);

        Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }

    // POST /api/auth/verify-email
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        await mediator.Send(new VerifyEmailCommand(request.Token), ct);
        return NoContent();
    }

    // POST /api/auth/resend-verification
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        await mediator.Send(new ResendVerificationEmailCommand(request.Email), ct);
        return NoContent();
    }

    // POST /api/auth/forgot-password
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await mediator.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Ok(new
        {
            message = "If this email is registered, you'll receive a reset link shortly.",
        });
    }

    // POST /api/auth/reset-password
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await mediator.Send(
            new ResetPasswordCommand(request.Token, request.NewPassword, request.ConfirmPassword), ct);
        return NoContent();
    }

    private async Task<IActionResult> IssueTokensAsync(
        Guid userId, Guid orgId, string email, string fullName,
        IReadOnlyList<string> permissions,
        string deviceInfo, CancellationToken ct)
    {
        var (rawRefresh, refreshHash) = tokenService.GenerateRefreshToken();
        var ttlDays = int.Parse(configuration["RefreshToken:TtlDays"] ?? "7");
        var refreshExpires = DateTime.UtcNow.AddDays(ttlDays);

        var refreshId = await refreshTokenStore.CreateAsync(
            userId, orgId, refreshHash, deviceInfo, refreshExpires, ct);

        var accessToken = tokenService.GenerateAccessToken(
            userId, orgId, email, fullName, permissions, refreshId);

        Response.Cookies.Append("refresh_token", rawRefresh, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = refreshExpires,
        });

        return Ok(new
        {
            access_token = accessToken.Token,
            token_type = "Bearer",
            expires_in = (int)(accessToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
        });
    }
}

public record SignInRequest(string Email, string Password);
public record VerifyEmailRequest(string Token);
public record ResendVerificationRequest(string Email);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
