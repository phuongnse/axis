using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Commands.RegisterOrganization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations");

        group.MapPost("/", Register);
        group.MapPost("/me/invitations", InviteUser)
            .RequireAuthorization(Permissions.Users.Invite);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterOrganizationRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new RegisterOrganizationCommand(
            request.OrgName,
            request.AdminFirstName,
            request.AdminLastName,
            request.AdminEmail,
            request.Password,
            request.PasswordConfirmation), ct);

        return Results.Ok(new
        {
            message = "Registration successful. Please check your email to verify your account.",
        });
    }

    private static async Task<IResult> InviteUser(
        [FromBody] InviteUserRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        if (string.Equals(request.Email, currentUser.Email, StringComparison.OrdinalIgnoreCase))
            return Results.UnprocessableEntity(new
            {
                error = "validation_failed",
                errors = new { email = new[] { "You cannot invite yourself." } },
            });

        await mediator.Send(new InviteUserCommand(
            currentUser.OrgId,
            request.Email,
            request.RoleId,
            currentUser.UserId), ct);

        return Results.Ok(new { message = $"Invitation sent to {request.Email}." });
    }
}

public record RegisterOrganizationRequest(
    string OrgName,
    string AdminFirstName,
    string AdminLastName,
    string AdminEmail,
    string Password,
    string PasswordConfirmation);

public record InviteUserRequest(string Email, Guid RoleId);
