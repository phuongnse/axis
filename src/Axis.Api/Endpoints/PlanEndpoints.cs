using Axis.Api.Infrastructure;
using Axis.Identity.Application.Queries.ListSubscriptionPlans;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/plans");

        group.MapGet("/", ListPlans)
            .AllowAnonymous()
            .WithName("ListSubscriptionPlans")
            .WithSummary("List subscription plans available for signup")
            .WithTags("Platform")
            .Produces<IReadOnlyList<SubscriptionPlanDto>>();

        return app;
    }

    private static async Task<IResult> ListPlans(
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        Guid? workspaceId = null;
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            string? workspaceClaim = httpContext.User.FindFirst("workspace_id")?.Value;
            if (workspaceClaim is not null && Guid.TryParse(workspaceClaim, out Guid parsedWorkspaceId))
                workspaceId = parsedWorkspaceId;
        }
        IReadOnlyList<SubscriptionPlanDto> plans = await mediator.Send(
            new ListSubscriptionPlansQuery(workspaceId),
            ct);
        return Results.Ok(plans);
    }
}
