using System.Security.Claims;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Authorization;

internal sealed class WorkspaceAccessRequirement : IAuthorizationRequirement;

internal sealed class WorkspaceAccessAuthorizationHandler(
    IWorkspaceMembershipRepository memberships,
    IWorkspaceContextTransitionRepository transitions,
    IServiceClientAssertionAuthentication serviceAuthority,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<WorkspaceAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkspaceAccessRequirement requirement)
    {
        if (string.Equals(context.User.FindFirst("subject_kind")?.Value, "service", StringComparison.Ordinal))
        {
            if (httpContextAccessor.HttpContext?.GetEndpoint()?.Metadata
                    .GetMetadata<ServiceProductEndpointMetadata>() is null
                || !Guid.TryParse(context.User.GetClaim(Claims.Subject), out Guid serviceId)
                || !Guid.TryParse(context.User.FindFirstValue("workspace_id"), out _)
                || !Guid.TryParse(context.User.FindFirstValue("service_key_id"), out Guid keyId)
                || !await serviceAuthority.HasActiveAuthorityAsync(serviceId, keyId))
            {
                return;
            }

            context.Succeed(requirement);
            return;
        }

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
