using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.ChangeOrganizationPlan;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class PlatformOrganizationEndpoints
{
    public static IEndpointRouteBuilder MapPlatformOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/platform/organizations")
            .RequireAuthorization();

        group.MapPut("/{organizationId:guid}/plan", ChangePlan)
            .WithName("ChangeOrganizationPlan")
            .WithSummary("Platform admin: change an organization's subscription plan (US-012)")
            .WithTags("Platform")
            .Produces<object>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> ChangePlan(
        Guid organizationId,
        [FromBody] ChangeOrganizationPlanRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ChangeOrganizationPlanCommand(organizationId, request.PlanId, currentUser.UserId),
            ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new { message = "Organization plan updated." });
    }
}
