using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Commands.RegisterOrganization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class OrganizationsController(ISender mediator) : ControllerBase
{
    // POST /api/organizations — US-001
    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterOrganizationRequest request, CancellationToken ct)
    {
        await mediator.Send(new RegisterOrganizationCommand(
            request.OrgName,
            request.AdminFirstName,
            request.AdminLastName,
            request.AdminEmail,
            request.Password,
            request.PasswordConfirmation), ct);

        return Ok(new
        {
            message = "Registration successful. Please check your email to verify your account.",
        });
    }

    // POST /api/organizations/me/invitations — US-017
    [Authorize(Policy = Permissions.Users.Invite)]
    [HttpPost("me/invitations")]
    public async Task<IActionResult> InviteUser(
        [FromBody] InviteUserRequest request,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        // Guard: admin cannot invite themselves (US-017 AC)
        if (string.Equals(request.Email, currentUser.Email, StringComparison.OrdinalIgnoreCase))
            return UnprocessableEntity(new
            {
                error = "validation_failed",
                errors = new { email = new[] { "You cannot invite yourself." } },
            });

        await mediator.Send(new InviteUserCommand(
            currentUser.OrgId,
            request.Email,
            request.RoleId,
            currentUser.UserId), ct);

        return Ok(new { message = $"Invitation sent to {request.Email}." });
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
