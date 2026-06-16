using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.ChangeWorkspacePlan;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class PlatformWorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapPlatformWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/platform/workspaces")
            .RequireAuthorization();

        group.MapPut("/{workspaceId:guid}/plan", ChangePlan)
            .WithName("ChangeWorkspacePlan")
            .WithSummary("Platform admin: change a workspace's subscription plan")
            .WithTags("Platform")
            .Produces<MessageResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> ChangePlan(
        Guid workspaceId,
        [FromBody] ChangeWorkspacePlanRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ChangeWorkspacePlanCommand(workspaceId, request.PlanId, currentUser.UserId),
            ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse("Workspace plan updated."));
    }
}
