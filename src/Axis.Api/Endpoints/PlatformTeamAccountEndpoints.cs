using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.ChangeTeamAccountPlan;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class PlatformTeamAccountEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTeamAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/platform/team-accounts")
            .RequireAuthorization();

        group.MapPut("/{teamAccountId:guid}/plan", ChangePlan)
            .WithName("ChangeTeamAccountPlan")
            .WithSummary("Platform admin: change a team account's subscription plan")
            .WithTags("Platform")
            .Produces<MessageResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> ChangePlan(
        Guid teamAccountId,
        [FromBody] ChangeTeamAccountPlanRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ChangeTeamAccountPlanCommand(teamAccountId, request.PlanId, currentUser.UserId),
            ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse("Team account plan updated."));
    }
}
