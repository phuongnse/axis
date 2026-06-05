using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AssignRoleToUser;
using Axis.Identity.Application.Commands.ChangePassword;
using Axis.Identity.Application.Commands.DeactivateUser;
using Axis.Identity.Application.Commands.RegisterUser;
using Axis.Identity.Application.Commands.RevokeSession;
using Axis.Identity.Application.Commands.UpdateUserProfile;
using Axis.Identity.Application.Queries.GetCurrentUserProfile;
using Axis.Identity.Application.Queries.GetUserSessions;
using Axis.Identity.Application.Services;
using Axis.Shared.Application;
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
        RouteGroupBuilder publicGroup = app.MapGroup("/api/users");

        publicGroup.MapPost("/register", Register)
            .AllowAnonymous()
            .WithName("RegisterUser")
            .WithSummary("Register a standalone user account")
            .WithTags("Identity")
            .Produces<MessageResponse>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        RouteGroupBuilder group = publicGroup.MapGroup("").RequireAuthorization();

        group.MapGet("/me", GetMe)
            .WithName("GetMe")
            .WithSummary("Get the current user's profile")
            .WithTags("Identity")
            .Produces<CurrentUserProfileDto>()
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
            .Produces<IReadOnlyList<UserSessionResponse>>()
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

    private static async Task<IResult> Register(
        [FromBody] RegisterUserRequest request,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        string? idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

        Result result = await mediator.Send(new RegisterUserCommand(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Password,
            request.PasswordConfirmation,
            request.AcceptedTermsVersion,
            request.AcceptedPrivacyVersion,
            request.OrganizationSetupToken,
            idempotencyKey), ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse(
            "Registration successful. Please check your email to verify your account."));
    }

    private static async Task<IResult> GetMe(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        CurrentUserProfileDto? profile = await mediator.Send(
            new GetCurrentUserProfileQuery(
                currentUser.UserId,
                currentUser.OrgIdOrNull,
                currentUser.Permissions),
            ct);
        if (profile is null)
            return Results.NotFound();

        return Results.Ok(profile);
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

        return Results.Ok(sessions.Select(s => new UserSessionResponse(
            s.SessionId,
            s.DeviceInfo,
            s.LastActivity,
            s.ExpiresAt,
            s.IsCurrentSession)));
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
        CancellationToken ct)
    {
        if (userId == currentUser.UserId)
            return Results.Problem(
                "You cannot deactivate yourself.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (!request.IsActive)
        {
            Result deactivateResult = await mediator.Send(
                new DeactivateUserCommand(userId, currentUser.OrgId, currentUser.UserId),
                ct);

            if (deactivateResult.IsFailure) return deactivateResult.ToProblemDetails();
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
