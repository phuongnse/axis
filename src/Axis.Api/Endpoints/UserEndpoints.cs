using Axis.Api.Authorization;
using Axis.Api.Extensions;
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
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/me", GetMe)
            .WithName("GetMe")
            .WithSummary("Get the current user's profile")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        group.MapPatch("/me", UpdateProfile)
            .WithName("UpdateProfile")
            .WithSummary("Update the current user's profile")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(422);

        group.MapPost("/me/change-password", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Change the current user's password")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(422);

        group.MapGet("/me/sessions", GetSessions)
            .WithName("GetUserSessions")
            .WithSummary("List active sessions for the current user")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(401);

        group.MapDelete("/me/sessions/{sessionId}", RevokeSession)
            .WithName("RevokeSession")
            .WithSummary("Revoke a specific session by ID")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401);

        group.MapDelete("/me/sessions", RevokeAllSessions)
            .WithName("RevokeAllSessions")
            .WithSummary("Revoke all sessions for the current user")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401);

        group.MapPatch("/{userId:guid}/status", UpdateStatus)
            .RequireAuthorization(Permissions.Users.Deactivate)
            .WithName("UpdateUserStatus")
            .WithSummary("Activate or deactivate a user")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapPut("/{userId:guid}/roles", AssignRole)
            .RequireAuthorization(Permissions.Roles.Write)
            .WithName("AssignRoleToUser")
            .WithSummary("Assign or remove a role from a user")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> GetMe(
        CurrentUser currentUser,
        IUserRepository userRepository,
        CancellationToken ct)
    {
        User? user = await userRepository.GetByIdPlatformWideAsync(currentUser.UserId, ct);
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

        Result result = await mediator.Send(new UpdateUserProfileCommand(
            currentUser.UserId,
            currentUser.OrgId,
            request.FirstName,
            request.LastName,
            avatarBytes,
            avatarContentType), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ChangePasswordCommand(
            currentUser.UserId,
            currentUser.OrgId,
            request.CurrentPassword,
            request.NewPassword,
            request.ConfirmPassword), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> GetSessions(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        // currentTokenId identifies the current session so the UI can mark it as "current device".
        // With OpenIddict the access token doesn't carry the refresh token ID — pass empty string
        // until we add a custom "rt_id" claim at issuance time (tracked gap).
        IReadOnlyList<UserSession> sessions = await mediator.Send(
            new GetUserSessionsQuery(currentUser.UserId, CurrentTokenId: string.Empty), ct);

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
        Result result = await mediator.Send(new RevokeSessionCommand(sessionId, currentUser.UserId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllSessions(
        CurrentUser currentUser,
        ISender mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new RevokeSessionCommand(null, currentUser.UserId), ct);
        if (result.IsFailure) return result.ToProblemDetails();

        // Clear the refresh token cookie from this device
        httpContext.Response.Cookies.Delete("refresh_token",
            new CookieOptions { Path = "/connect" });

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateStatus(
        Guid userId,
        [FromBody] UpdateStatusRequest request,
        CurrentUser currentUser,
        ISender mediator,
        IRoleRepository roleRepository,
        ISessionStore sessionStore,
        CancellationToken ct)
    {
        if (userId == currentUser.UserId)
            return Results.Problem(
                "You cannot deactivate yourself.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (!request.IsActive)
        {
            Role? adminRole = await roleRepository.GetByNameAsync("Admin", currentUser.OrgId, ct);
            Guid adminRoleId = adminRole?.Id ?? Guid.Empty;

            Result deactivateResult = await mediator.Send(new DeactivateUserCommand(
                userId, currentUser.OrgId, currentUser.UserId, adminRoleId), ct);

            if (deactivateResult.IsFailure) return deactivateResult.ToProblemDetails();

            // Revoke all active refresh tokens so the deactivated user can't silently renew
            await sessionStore.RevokeAllAsync(userId, ct);
        }
        else
        {
            return Results.Problem(
                "Reactivation is not yet implemented.",
                statusCode: StatusCodes.Status501NotImplemented);
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
        Result result = await mediator.Send(new AssignRoleToUserCommand(
            userId,
            currentUser.OrgId,
            request.RoleId,
            request.Action == "assign" ? RoleAction.Assign : RoleAction.Remove), ct);

        if (result.IsFailure) return result.ToProblemDetails();
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
