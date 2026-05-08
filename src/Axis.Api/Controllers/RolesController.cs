using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CreateRole;
using Axis.Identity.Application.Commands.UpdateRole;
using Axis.Identity.Application.Queries.GetRoles;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(ISender mediator) : ControllerBase
{
    // GET /api/roles — US-021
    [Authorize(Policy = Permissions.Roles.Read)]
    [HttpGet]
    public async Task<IActionResult> GetRoles(
        [FromServices] CurrentUser currentUser, CancellationToken ct)
    {
        var roles = await mediator.Send(new GetRolesQuery(currentUser.OrgId), ct);

        return Ok(roles.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            description = r.Description,
            is_system = r.IsSystem,
            permissions = r.Permissions,
            permission_count = r.Permissions.Count
        }));
    }

    // POST /api/roles — US-022
    [Authorize(Policy = Permissions.Roles.Write)]
    [HttpPost]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateRoleRequest request,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        var roleId = await mediator.Send(new CreateRoleCommand(
            currentUser.OrgId,
            request.Name,
            request.Description,
            request.Permissions), ct);

        return Ok(new { id = roleId, message = $"Role '{request.Name}' created." });
    }

    // PUT /api/roles/{roleId} — US-023
    [Authorize(Policy = Permissions.Roles.Write)]
    [HttpPut("{roleId:guid}")]
    public async Task<IActionResult> UpdateRole(
        Guid roleId,
        [FromBody] UpdateRoleRequest request,
        [FromServices] CurrentUser currentUser,
        CancellationToken ct)
    {
        await mediator.Send(new UpdateRoleCommand(
            roleId,
            currentUser.OrgId,
            request.Name,
            request.Description,
            request.Permissions), ct);

        return NoContent();
    }
}

public record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
public record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
