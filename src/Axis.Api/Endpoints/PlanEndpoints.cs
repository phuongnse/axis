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
        Guid? teamAccountId = null;
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            string? teamAccountClaim = httpContext.User.FindFirst("team_account_id")?.Value;
            if (teamAccountClaim is not null && Guid.TryParse(teamAccountClaim, out Guid parsedTeamAccountId))
                teamAccountId = parsedTeamAccountId;
        }
        IReadOnlyList<SubscriptionPlanDto> plans = await mediator.Send(
            new ListSubscriptionPlansQuery(teamAccountId),
            ct);
        return Results.Ok(plans);
    }
}
