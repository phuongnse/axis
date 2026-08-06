using System.Security.Claims;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Authorization;

internal sealed class WorkspaceAccessRequirement : IAuthorizationRequirement;

internal sealed class WorkspaceAccessAuthorizationHandler(
    IWorkspaceMembershipRepository memberships,
    IWorkspaceContextTransitionRepository transitions)
    : AuthorizationHandler<WorkspaceAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkspaceAccessRequirement requirement)
    {
        if (!Guid.TryParse(context.User.GetClaim(Claims.Subject), out Guid userId)
            || !Guid.TryParse(context.User.FindFirstValue("workspace_id"), out Guid workspaceId)
            || !await memberships.HasActiveWorkspaceAccessAsync(workspaceId, userId))
        {
            return;
        }

        bool browserSession = context.User.Identities.Any(identity =>
            identity.IsAuthenticated
            && StringComparer.Ordinal.Equals(
                identity.AuthenticationType,
                CookieAuthenticationDefaults.AuthenticationScheme));
        if (!browserSession)
        {
            context.Succeed(requirement);
            return;
        }

        string? correlation = context.User.FindFirstValue(BrowserSessionCorrelation.ClaimType);
        if (string.IsNullOrWhiteSpace(correlation))
            return;

        string digest = BrowserSessionCorrelation.Digest(correlation);
        WorkspaceContextTransition? source =
            await transitions.GetBySourceCorrelationDigestAsync(userId, digest);
        if (source?.Status is
            WorkspaceContextTransitionStatus.Pending or WorkspaceContextTransitionStatus.Completed)
        {
            return;
        }

        WorkspaceContextTransition? target =
            await transitions.GetByTargetCorrelationDigestAsync(userId, digest);
        if (target is not null
            && (target.Status != WorkspaceContextTransitionStatus.Completed
                || target.TargetWorkspaceId != workspaceId))
        {
            return;
        }

        context.Succeed(requirement);
    }
}
