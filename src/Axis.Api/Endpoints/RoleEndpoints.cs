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
            .RequireAuthorization(Permissions.Roles.Read);
        group.MapPost("/", CreateRole)
            .RequireAuthorization(Permissions.Roles.Write);
        group.MapPut("/{roleId:guid}", UpdateRole)
            .RequireAuthorization(Permissions.Roles.Write);

        return app;
    }

    private static async Task<IResult> GetRoles(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await mediator.Send(new GetRolesQuery(currentUser.OrgId, page, pageSize), ct);
        return Results.Ok(result);
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
