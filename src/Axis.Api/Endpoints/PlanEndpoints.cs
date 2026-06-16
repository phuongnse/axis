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
        Guid? tenantId = null;
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            string? tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            if (tenantClaim is not null && Guid.TryParse(tenantClaim, out Guid parsedTenantId))
                tenantId = parsedTenantId;
        }
        IReadOnlyList<SubscriptionPlanDto> plans = await mediator.Send(
            new ListSubscriptionPlansQuery(tenantId),
            ct);
        return Results.Ok(plans);
    }
}
