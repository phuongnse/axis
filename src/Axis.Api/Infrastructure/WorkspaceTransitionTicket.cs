using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Infrastructure;

internal sealed record WorkspaceTransitionTicket(
    Guid TransitionId,
    Guid UserId,
    Guid SourceWorkspaceId,
    Guid TargetWorkspaceId,
    string SourceCorrelation,
    string TargetCorrelation)
{
    private const string TransitionIdClaim = "axis:transition_id";
    private const string SourceWorkspaceIdClaim = "axis:source_workspace_id";
    private const string TargetWorkspaceIdClaim = "axis:target_workspace_id";
    private const string SourceCorrelationClaim = "axis:source_correlation";
    internal const string TargetCorrelationClaim = "axis:target_correlation";

    public ClaimsPrincipal CreatePrincipal() =>
        new(new ClaimsIdentity(
        [
            new Claim(Claims.Subject, UserId.ToString()),
            new Claim(TransitionIdClaim, TransitionId.ToString()),
            new Claim(SourceWorkspaceIdClaim, SourceWorkspaceId.ToString()),
            new Claim("workspace_id", SourceWorkspaceId.ToString()),
            new Claim(TargetWorkspaceIdClaim, TargetWorkspaceId.ToString()),
            new Claim(SourceCorrelationClaim, SourceCorrelation),
            new Claim(BrowserSessionCorrelation.ClaimType, SourceCorrelation),
            new Claim(TargetCorrelationClaim, TargetCorrelation),
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static bool TryRead(AuthenticateResult result, out WorkspaceTransitionTicket? ticket)
    {
        ClaimsPrincipal? principal = result.Succeeded ? result.Principal : null;
        if (principal is null
            || !Guid.TryParse(principal.GetClaim(TransitionIdClaim), out Guid transitionId)
            || !Guid.TryParse(principal.GetClaim(Claims.Subject), out Guid userId)
            || !Guid.TryParse(principal.GetClaim(SourceWorkspaceIdClaim), out Guid sourceWorkspaceId)
            || !Guid.TryParse(principal.GetClaim(TargetWorkspaceIdClaim), out Guid targetWorkspaceId))
        {
            ticket = null;
            return false;
        }

        string? sourceCorrelation = principal.GetClaim(SourceCorrelationClaim);
        string? targetCorrelation = principal.GetClaim(TargetCorrelationClaim);
        if (string.IsNullOrWhiteSpace(sourceCorrelation)
            || string.IsNullOrWhiteSpace(targetCorrelation))
        {
            ticket = null;
            return false;
        }

        ticket = new WorkspaceTransitionTicket(
            transitionId,
            userId,
            sourceWorkspaceId,
            targetWorkspaceId,
            sourceCorrelation,
            targetCorrelation);
        return true;
    }
}
