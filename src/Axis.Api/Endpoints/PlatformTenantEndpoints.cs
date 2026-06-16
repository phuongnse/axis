using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.ChangeTenantPlan;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class PlatformTenantEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTenantEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/platform/tenants")
            .RequireAuthorization();

        group.MapPut("/{tenantId:guid}/plan", ChangePlan)
            .WithName("ChangeTenantPlan")
            .WithSummary("Platform admin: change a tenant's subscription plan")
            .WithTags("Platform")
            .Produces<MessageResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> ChangePlan(
        Guid tenantId,
        [FromBody] ChangeTenantPlanRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ChangeTenantPlanCommand(tenantId, request.PlanId, currentUser.UserId),
            ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse("Tenant plan updated."));
    }
}
