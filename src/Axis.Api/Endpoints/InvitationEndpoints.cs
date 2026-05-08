using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AcceptInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invitations");

        group.MapGet("/{token}", GetInvitation);
        group.MapPost("/{token}/accept", AcceptInvitation);

        return app;
    }

    private static async Task<IResult> GetInvitation(
        string token,
        IInvitationRepository invitationRepository,
        CancellationToken ct)
    {
        var invitation = await invitationRepository.GetByTokenAsync(token, ct);
        if (invitation is null)
            return Results.NotFound(new { error = "invitation_not_found" });

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
        var result = await mediator.Send(
            new AcceptInvitationCommand(token, request.FirstName, request.LastName, request.Password), ct);

        return await TokenHelper.IssueTokensAsync(
            result.UserId, result.OrganizationId,
            result.Email, result.FullName,
            result.Permissions,
            tokenService, refreshTokenStore, configuration, httpContext, ct);
    }
}

public record AcceptInvitationRequest(string FirstName, string LastName, string Password);
