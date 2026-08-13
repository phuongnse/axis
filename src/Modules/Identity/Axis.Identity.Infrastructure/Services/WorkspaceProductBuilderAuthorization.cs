using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WorkspaceProductBuilderAuthorization(IdentityDbContext context)
    : IWorkspaceProductBuilderAuthorization
{
    public async Task<WorkspaceProductBuilderDecision> AuthorizeAsync(
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || subject.Kind != SubjectKind.Human || subject.Id == Guid.Empty)
            return WorkspaceProductBuilderDecision.Denied;

        try
        {
            bool allowed = await context.WorkspaceMemberships
                .AsNoTracking()
                .AnyAsync(membership =>
                    membership.WorkspaceId == workspaceId
                    && membership.UserId == subject.Id
                    && membership.Status == MembershipStatus.Active
                    && membership.IsProductBuilder
                    && context.Users.Any(user =>
                        user.Id == subject.Id && user.Status == UserStatus.Active)
                    && context.Workspaces.Any(workspace =>
                        workspace.Id == workspaceId && workspace.Status == WorkspaceStatus.Active),
                    cancellationToken);
            return allowed
                ? WorkspaceProductBuilderDecision.Allowed
                : WorkspaceProductBuilderDecision.Denied;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkspaceProductBuilderDecision.Unavailable;
        }
    }
}
