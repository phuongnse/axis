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
            .WithSummary("List subscription plans available for signup (US-010)")
            .WithTags("Platform")
            .Produces<IReadOnlyList<SubscriptionPlanDto>>();

        return app;
    }

    private static async Task<IResult> ListPlans(
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        Guid? orgId = null;
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            string? orgClaim = httpContext.User.FindFirst("org_id")?.Value;
            if (orgClaim is not null && Guid.TryParse(orgClaim, out Guid parsedOrgId))
                orgId = parsedOrgId;
        }
        IReadOnlyList<SubscriptionPlanDto> plans = await mediator.Send(
            new ListSubscriptionPlansQuery(orgId),
            ct);
        return Results.Ok(plans);
    }
}
