using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.InviteUser;
using Axis.Identity.Application.Commands.RegisterTeamAccount;
using Axis.Identity.Application.Queries.GetTeamAccountSlugPreview;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class TeamAccountEndpoints
{
    public static IEndpointRouteBuilder MapTeamAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/team-accounts");

        group.MapGet("/slug-preview", GetSlugPreview)
            .AllowAnonymous()
            .WithName("GetTeamAccountSlugPreview")
            .WithSummary("Preview team account URL slug from a proposed name")
            .WithTags("Identity")
            .Produces<TeamAccountSlugPreviewDto>();

        group.MapPost("/", Register)
            .AllowAnonymous()
            .WithName("RegisterTeamAccount")
            .WithSummary("Register a new team account contact for verification")
            .WithTags("Identity")
            .Produces<MessageResponse>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        group.MapPost("/me/invitations", InviteUser)
            .RequireAuthorization(Permissions.Users.Invite)
            .WithName("InviteUser")
            .WithSummary("Invite a user to the team account by email")
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
        [FromQuery(Name = "teamAccountName")] string teamAccountName,
        ISender mediator,
        CancellationToken ct)
    {
        TeamAccountSlugPreviewDto preview =
            await mediator.Send(new GetTeamAccountSlugPreviewQuery(teamAccountName), ct);
        return Results.Ok(preview);
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterTeamAccountRequest request,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken ct)
    {
        string? idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

        Result result = await mediator.Send(new RegisterTeamAccountCommand(
            request.TeamAccountName,
            request.TeamContactEmail,
            request.AcceptedTermsVersion,
            request.AcceptedPrivacyVersion,
            request.SubscriptionPlanId,
            idempotencyKey), ct);

        if (result.IsFailure)
            return result.ToProblemDetails();

        return Results.Ok(new MessageResponse(
            "Registration successful. Please check your email to verify your team account."));
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
            currentUser.TeamAccountId,
            request.Email,
            request.RoleId,
            currentUser.UserId), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(new MessageResponse($"Invitation sent to {request.Email}."));
    }
}
