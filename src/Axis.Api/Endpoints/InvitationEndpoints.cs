using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AcceptInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/invitations");

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
            .Produces<object>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetInvitation(
        string token,
        IInvitationRepository invitationRepository,
        CancellationToken ct)
    {
        Invitation? invitation = await invitationRepository.GetByTokenAsync(token, ct);
        if (invitation is null)
            return Results.Problem("Invitation not found.", statusCode: StatusCodes.Status404NotFound);

        return Results.Ok(new
        {
            invitation_id = invitation.Id,
            email = invitation.Email.Value,
            status = invitation.Status.ToString().ToLower(),
            expires_at = invitation.ExpiresAt,
        });
    }

    private static async Task<IResult> AcceptInvitation(
        string token,
        [FromBody] AcceptInvitationRequest request,
        ISender mediator,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Result<AcceptInvitationResult> result = await mediator.Send(
            new AcceptInvitationCommand(token, request.FirstName, request.LastName, request.Password), ct);

        if (result.IsFailure) return result.ToProblemDetails();

        return await TokenHelper.IssueTokensAsync(
            result.Value.UserId, result.Value.OrganizationId,
            result.Value.Email, result.Value.FullName,
            result.Value.Permissions,
            tokenService, refreshTokenStore, configuration, httpContext, ct);
    }
}

public record AcceptInvitationRequest(string FirstName, string LastName, string Password);
