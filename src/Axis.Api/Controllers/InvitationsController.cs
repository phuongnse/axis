using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.AcceptInvitation;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Controllers;

[ApiController]
[Route("api/invitations")]
public class InvitationsController(ISender mediator, IInvitationRepository invitationRepository) : ControllerBase
{
    // GET /api/invitations/{token} — US-018 preview
    [HttpGet("{token}")]
    public async Task<IActionResult> GetInvitation(string token, CancellationToken ct)
    {
        var invitation = await invitationRepository.GetByTokenAsync(token, ct);
        if (invitation is null)
            return NotFound(new { error = "invitation_not_found" });

        return Ok(new
        {
            invitation_id = invitation.Id,
            email = invitation.Email.Value,
            status = invitation.Status.ToString().ToLower(),
            expires_at = invitation.ExpiresAt,
        });
    }

    // POST /api/invitations/{token}/accept — US-018
    [HttpPost("{token}/accept")]
    public async Task<IActionResult> AcceptInvitation(
        string token,
        [FromBody] AcceptInvitationRequest request,
        [FromServices] ITokenService tokenService,
        [FromServices] IRefreshTokenStore refreshTokenStore,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new AcceptInvitationCommand(token, request.FirstName, request.LastName, request.Password), ct);

        var (rawRefresh, refreshHash) = tokenService.GenerateRefreshToken();
        var ttlDays = int.Parse(configuration["RefreshToken:TtlDays"] ?? "7");
        var refreshExpires = DateTime.UtcNow.AddDays(ttlDays);

        var refreshId = await refreshTokenStore.CreateAsync(
            result.UserId, result.OrganizationId, refreshHash,
            Request.Headers.UserAgent.ToString(), refreshExpires, ct);

        var accessToken = tokenService.GenerateAccessToken(
            result.UserId, result.OrganizationId,
            result.Email, result.FullName,
            result.Permissions, refreshId);

        Response.Cookies.Append("refresh_token", rawRefresh, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = refreshExpires,
        });

        return Ok(new
        {
            access_token = accessToken.Token,
            token_type = "Bearer",
            expires_in = (int)(accessToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
        });
    }
}

public record AcceptInvitationRequest(string FirstName, string LastName, string Password);
