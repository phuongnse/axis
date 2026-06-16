using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Commands.RegisterTenant;
using Axis.Identity.Application.Queries.GetTenantSlugPreview;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/tenants");

        group.MapGet("/slug-preview", GetSlugPreview)
            .AllowAnonymous()
            .WithName("GetTenantSlugPreview")
            .WithSummary("Preview tenant URL slug from a proposed name")
            .WithTags("Identity")
            .Produces<TenantSlugPreviewDto>();

        group.MapPost("/", Register)
            .AllowAnonymous()
            .WithName("RegisterTenant")
            .WithSummary("Register a new tenant contact for verification")
            .WithTags("Identity")
            .Produces<MessageResponse>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        group.MapPost("/me/invitations", InviteUser)
            .RequireAuthorization(Permissions.Users.Invite)
            .WithName("InviteUser")
            .WithSummary("Invite a user to the tenant by email")
            .WithTags("Identity")
            .Produces<MessageResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetSlugPreview(
        [FromQuery(Name = "tenantName")] string tenantName,
        ISender mediator,
        CancellationToken ct)
    {
        TenantSlugPreviewDto preview =
            await mediator.Send(new GetTenantSlugPreviewQuery(tenantName), ct);
        return Results.Ok(preview);
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterTenantRequest request,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        string? idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

        Result result = await mediator.Send(new RegisterTenantCommand(
            request.TenantName,
            request.TenantContactEmail,
            request.AcceptedTermsVersion,
            request.AcceptedPrivacyVersion,
            request.SubscriptionPlanId,
            idempotencyKey), ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse(
            "Registration successful. Please check your email to verify your tenant."));
    }

    private static async Task<IResult> InviteUser(
        [FromBody] InviteUserRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        if (string.Equals(request.Email, currentUser.Email, StringComparison.OrdinalIgnoreCase))
            return Results.Problem("You cannot invite yourself.", statusCode: StatusCodes.Status422UnprocessableEntity);

        Result result = await mediator.Send(new InviteUserCommand(
            currentUser.TenantId,
            request.Email,
            request.RoleId,
            currentUser.UserId), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(new MessageResponse($"Invitation sent to {request.Email}."));
    }
}
