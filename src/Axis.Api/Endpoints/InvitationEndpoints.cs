using Axis.Api.Extensions;
using Axis.Identity.Application.Commands.AcceptInvitation;
using Axis.Identity.Application.Queries.GetInvitationByToken;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        // The {token} segment IS the auth factor for these endpoints (one-time
        // signed invitation link). Mark the group AllowAnonymous so route-level
        // auth metadata is explicit; individual endpoints validate token + email
        // inside their handlers.
        RouteGroupBuilder group = app.MapGroup("/api/invitations").AllowAnonymous();

        group.MapGet("/{token}", GetInvitation)
            .WithName("GetInvitation")
            .WithSummary("Get invitation details by token")
            .WithTags("Identity")
            .Produces<object>()
            .ProducesProblem(404);

        group.MapPost("/{token}/accept", AcceptInvitation)
            .WithName("AcceptInvitation")
            .WithSummary("Accept an invitation and create a user account")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetInvitation(
        string token,
        ISender mediator,
        CancellationToken ct)
    {
        InvitationByTokenDto? invitation = await mediator.Send(new GetInvitationByTokenQuery(token), ct);
        if (invitation is null)
            return Results.Problem("Invitation not found.", statusCode: StatusCodes.Status404NotFound);

        return Results.Ok(new
        {
            invitation_id = invitation.InvitationId,
            email = invitation.Email,
            status = invitation.Status,
            expires_at = invitation.ExpiresAt,
        });
    }

    private static async Task<IResult> AcceptInvitation(
        string token,
        [FromBody] AcceptInvitationRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<AcceptInvitationResult> result = await mediator.Send(
            new AcceptInvitationCommand(token, request.FirstName, request.LastName, request.Password), ct);

        if (result.IsFailure) return result.ToProblemDetails();

        // Invitation accepted — client initiates the Authorization Code + PKCE flow
        // at POST /connect/login to sign in. Auto-sign-in is incompatible with PKCE.
        return Results.NoContent();
    }
}

public record AcceptInvitationRequest(string FirstName, string LastName, string Password);
