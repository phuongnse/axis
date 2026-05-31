using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Commands.RegisterOrganization;
using Axis.Identity.Application.Queries.GetOrganizationSlugPreview;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/organizations");

        group.MapGet("/slug-preview", GetSlugPreview)
            .AllowAnonymous()
            .WithName("GetOrganizationSlugPreview")
            .WithSummary("Preview organization URL slug from a proposed name")
            .WithTags("Identity")
            .Produces<OrganizationSlugPreviewDto>();

        group.MapPost("/", Register)
            .AllowAnonymous()
            .WithName("RegisterOrganization")
            .WithSummary("Register a new organization and admin account")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        group.MapPost("/me/invitations", InviteUser)
            .RequireAuthorization(Permissions.Users.Invite)
            .WithName("InviteUser")
            .WithSummary("Invite a user to the organization by email")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetSlugPreview(
        [FromQuery(Name = "org_name")] string orgName,
        ISender mediator,
        CancellationToken ct)
    {
        OrganizationSlugPreviewDto preview =
            await mediator.Send(new GetOrganizationSlugPreviewQuery(orgName), ct);
        return Results.Ok(preview);
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterOrganizationRequest request,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        string? idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

        Result result = await mediator.Send(new RegisterOrganizationCommand(
            request.OrgName,
            request.AdminFirstName,
            request.AdminLastName,
            request.AdminEmail,
            request.Password,
            request.PasswordConfirmation,
            request.AcceptedTermsVersion,
            request.AcceptedPrivacyVersion,
            request.SubscriptionPlanId,
            idempotencyKey), ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new
        {
            message = "Registration successful. Please check your email to verify your account.",
        });
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
            currentUser.OrgId,
            request.Email,
            request.RoleId,
            currentUser.UserId), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(new { message = $"Invitation sent to {request.Email}." });
    }
}
