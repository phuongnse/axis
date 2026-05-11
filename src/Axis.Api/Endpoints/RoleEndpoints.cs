using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CreateRole;
using Axis.Identity.Application.Commands.UpdateRole;
using Axis.Identity.Application.Queries.GetRoles;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").RequireAuthorization();

        group.MapGet("/", GetRoles)
            .RequireAuthorization(Permissions.Roles.Read)
            .WithName("GetRoles")
            .WithSummary("List all roles for the organization")
            .WithTags("Identity")
            .Produces<IReadOnlyList<RoleDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("/", CreateRole)
            .RequireAuthorization(Permissions.Roles.Write)
            .WithName("CreateRole")
            .WithSummary("Create a new role")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409);

        group.MapPut("/{roleId:guid}", UpdateRole)
            .RequireAuthorization(Permissions.Roles.Write)
            .WithName("UpdateRole")
            .WithSummary("Update a role's name, description, and permissions")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> GetRoles(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var roles = await mediator.Send(new GetRolesQuery(currentUser.OrgId), ct);

        return Results.Ok(roles.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            description = r.Description,
            is_system = r.IsSystem,
            permissions = r.Permissions,
            permission_count = r.Permissions.Count,
        }));
    }

    private static async Task<IResult> CreateRole(
        [FromBody] CreateRoleRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var roleId = await mediator.Send(new CreateRoleCommand(
            currentUser.OrgId,
            request.Name,
            request.Description,
            request.Permissions), ct);

        return Results.Ok(new { id = roleId, message = $"Role '{request.Name}' created." });
    }

    private static async Task<IResult> UpdateRole(
        Guid roleId,
        [FromBody] UpdateRoleRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new UpdateRoleCommand(
            roleId,
            currentUser.OrgId,
            request.Name,
            request.Description,
            request.Permissions), ct);

        return Results.NoContent();
    }
}

public record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
public record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
