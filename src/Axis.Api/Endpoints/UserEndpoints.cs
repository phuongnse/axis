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
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/me", GetMe);
        group.MapPatch("/me", UpdateProfile);
        group.MapPost("/me/change-password", ChangePassword);
        group.MapGet("/me/sessions", GetSessions);
        group.MapDelete("/me/sessions/{sessionId}", RevokeSession);
        group.MapDelete("/me/sessions", RevokeAllSessions);
        group.MapPatch("/{userId:guid}/status", UpdateStatus)
            .RequireAuthorization(Permissions.Users.Deactivate);
        group.MapPut("/{userId:guid}/roles", AssignRole)
            .RequireAuthorization(Permissions.Roles.Write);

        return app;
    }

    private static async Task<IResult> GetMe(
        CurrentUser currentUser,
        IUserRepository userRepository,
        CancellationToken ct)
    {
        var user = await userRepository.GetByIdPlatformWideAsync(currentUser.UserId, ct);
        if (user is null) return Results.NotFound();

        return Results.Ok(new
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

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CurrentUser currentUser,
        ISender mediator,
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

        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ChangePasswordCommand(
            currentUser.UserId,
            currentUser.OrgId,
            request.CurrentPassword,
            request.NewPassword,
            request.ConfirmPassword), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> GetSessions(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var sessions = await mediator.Send(
            new GetUserSessionsQuery(currentUser.UserId, currentUser.RefreshTokenId ?? string.Empty), ct);

        return Results.Ok(sessions.Select(s => new
        {
            session_id = s.SessionId,
            device_info = s.DeviceInfo,
            last_activity = s.LastActivity,
            expires_at = s.ExpiresAt,
            is_current = s.IsCurrentSession,
        }));
    }

    private static async Task<IResult> RevokeSession(
        string sessionId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new RevokeSessionCommand(sessionId, currentUser.UserId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllSessions(
        CurrentUser currentUser,
        ISender mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await mediator.Send(new RevokeSessionCommand(null, currentUser.UserId), ct);
        httpContext.Response.Cookies.Delete("refresh_token",
            new CookieOptions { Path = "/api/auth" });
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateStatus(
        Guid userId,
        [FromBody] UpdateStatusRequest request,
        CurrentUser currentUser,
        ISender mediator,
        IRoleRepository roleRepository,
        IRefreshTokenStore refreshTokenStore,
        CancellationToken ct)
    {
        if (userId == currentUser.UserId)
            return Results.UnprocessableEntity(new
            {
                error = "validation_failed",
                errors = new { user_id = new[] { "You cannot deactivate yourself." } },
            });

        if (!request.IsActive)
        {
            // DeactivateUserCommand enforces a last-admin guard: it rejects deactivation if
            // the target user is the last remaining Admin in the org. Passing adminRoleId
            // lets the handler check without querying the role table again.
            var adminRole = await roleRepository.GetByNameAsync("Admin", currentUser.OrgId, ct);
            var adminRoleId = adminRole?.Id ?? Guid.Empty;

            await mediator.Send(new DeactivateUserCommand(
                userId, currentUser.OrgId, currentUser.UserId, adminRoleId), ct);

            await refreshTokenStore.RevokeAllForUserAsync(userId, ct);
        }
        else
        {
            return Results.Json(new { error = "reactivation_not_implemented" }, statusCode: 501);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> AssignRole(
        Guid userId,
        [FromBody] AssignRoleRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new AssignRoleToUserCommand(
            userId,
            currentUser.OrgId,
            request.RoleId,
            request.Action == "assign" ? RoleAction.Assign : RoleAction.Remove), ct);

        return Results.NoContent();
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
