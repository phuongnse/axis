using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AssignRoleToUser;
using Axis.Identity.Application.Commands.ChangePassword;
using Axis.Identity.Application.Commands.DeactivateUser;
using Axis.Identity.Application.Commands.RevokeSession;
using Axis.Identity.Application.Commands.UpdateUserProfile;
using Axis.Identity.Application.Queries.GetUserSessions;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(ISender mediator, IUserRepository userRepository) : ControllerBase
{
    // GET /api/users/me — current user profile
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(
        [FromServices] CurrentUser currentUser, CancellationToken ct)
    {
        var user = await userRepository.GetByIdPlatformWideAsync(currentUser.UserId, ct);
        if (user is null) return NotFound();

        return Ok(new
        {
            id = user.Id,
            email = user.Email.Value,
            first_name = user.FirstName,
            last_name = user.LastName,
            full_name = $"{user.FirstName} {user.LastName}",
            avatar_url = user.AvatarUrl,
            is_active = user.Status == UserStatus.Active,
            org_id = currentUser.OrgId,
            permissions = currentUser.Permissions,
        });
    }

    // PATCH /api/users/me — update profile (US-020)
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        byte[]? avatarBytes = null;
        string? avatarContentType = null;

        if (request.AvatarBase64 is not null)
        {
            avatarBytes = Convert.FromBase64String(request.AvatarBase64);
            avatarContentType = request.AvatarContentType ?? "image/jpeg";
        }

        await mediator.Send(new UpdateUserProfileCommand(
            currentUser.UserId,
            currentUser.OrgId,
            request.FirstName,
            request.LastName,
            avatarBytes,
            avatarContentType), ct);

        return NoContent();
    }

    // POST /api/users/me/change-password — US-028
    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        await mediator.Send(new ChangePasswordCommand(
            currentUser.UserId,
            currentUser.OrgId,
            request.CurrentPassword,
            request.NewPassword,
            request.ConfirmPassword), ct);

        return NoContent();
    }

    // GET /api/users/me/sessions — US-029
    [HttpGet("me/sessions")]
    public async Task<IActionResult> GetSessions(
        [FromServices] CurrentUser currentUser, CancellationToken ct)
    {
        var sessions = await mediator.Send(
            new GetUserSessionsQuery(currentUser.UserId, currentUser.RefreshTokenId ?? string.Empty), ct);

        return Ok(sessions.Select(s => new
        {
            session_id = s.SessionId,
            device_info = s.DeviceInfo,
            last_activity = s.LastActivity,
            expires_at = s.ExpiresAt,
            is_current = s.IsCurrentSession,
        }));
    }

    // DELETE /api/users/me/sessions/{sessionId} — US-029 revoke specific
    [HttpDelete("me/sessions/{sessionId}")]
    public async Task<IActionResult> RevokeSession(
        string sessionId,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        await mediator.Send(new RevokeSessionCommand(sessionId, currentUser.UserId), ct);
        return NoContent();
    }

    // DELETE /api/users/me/sessions — US-029 sign out everywhere
    [HttpDelete("me/sessions")]
    public async Task<IActionResult> RevokeAllSessions(
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        await mediator.Send(new RevokeSessionCommand(null, currentUser.UserId), ct);
        Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }

    // PATCH /api/users/{userId}/status — US-019 deactivate/reactivate
    [Authorize(Policy = Permissions.Users.Deactivate)]
    [HttpPatch("{userId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid userId,
        [FromBody] UpdateStatusRequest request,
        [FromServices] CurrentUser currentUser,
        [FromServices] IRoleRepository roleRepository,
        [FromServices] IRefreshTokenStore refreshTokenStore,
        CancellationToken ct)
    {
        if (userId == currentUser.UserId)
            return UnprocessableEntity(new
            {
                error = "validation_failed",
                errors = new { user_id = new[] { "You cannot deactivate yourself." } },
            });

        if (!request.IsActive)
        {
            var adminRole = await roleRepository.GetByNameAsync("Admin", currentUser.OrgId, ct);
            var adminRoleId = adminRole?.Id ?? Guid.Empty;

            await mediator.Send(new DeactivateUserCommand(
                userId, currentUser.OrgId, currentUser.UserId, adminRoleId), ct);

            // Revoke all sessions for deactivated user (US-019 AC)
            await refreshTokenStore.RevokeAllForUserAsync(userId, ct);
        }
        else
        {
            // Reactivate is documented as a gap — no ReactivateUserCommand exists yet
            return StatusCode(501, new { error = "reactivation_not_implemented" });
        }

        return NoContent();
    }

    // PUT /api/users/{userId}/roles — US-024
    [Authorize(Policy = Permissions.Roles.Write)]
    [HttpPut("{userId:guid}/roles")]
    public async Task<IActionResult> AssignRole(
        Guid userId,
        [FromBody] AssignRoleRequest request,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        await mediator.Send(new AssignRoleToUserCommand(
            userId,
            currentUser.OrgId,
            request.RoleId,
            request.Action == "assign" ? RoleAction.Assign : RoleAction.Remove), ct);

        return NoContent();
    }
}

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? AvatarBase64,
    string? AvatarContentType);

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public record UpdateStatusRequest(bool IsActive);

public record AssignRoleRequest(Guid RoleId, string Action);
