using Axis.Authorization.Application;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;

namespace Axis.Api.Authorization;

internal sealed class IdentityAuthorizationSubjectActivity(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository serviceIdentities) : IAuthorizationSubjectActivity
{
    public async Task<bool> IsActiveAsync(
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken = default) =>
        subject.Kind switch
        {
            SubjectKind.Human => await memberships.GetActiveAsync(
                workspaceId,
                subject.Id,
                cancellationToken) is { Status: MembershipStatus.Active },
            SubjectKind.Service => await serviceIdentities.GetAsync(
                workspaceId,
                subject.Id,
                cancellationToken) is
            {
                Status: ServiceIdentityStatus.Active,
                WorkspaceGrantStatus: ServiceWorkspaceGrantStatus.Active,
            },
            _ => false,
        };
}

internal sealed class IdentityAuthorizationAdministratorAuthority(
    IWorkspaceMembershipRepository memberships) : IAuthorizationAdministratorAuthority
{
    public async Task<bool> IsAdministratorAsync(
        Guid workspaceId,
        SubjectReference actor,
        CancellationToken cancellationToken = default) =>
        actor.Kind == SubjectKind.Human
        && (await memberships.GetActiveAsync(workspaceId, actor.Id, cancellationToken))
            ?.HasLifecycleAdministratorAuthority is true;
}

internal sealed class IdentitySolutionAuthority(
    IWorkspaceMembershipRepository memberships) : ISolutionAuthority
{
    public async Task DemandAsync(
        SolutionActor actor,
        Guid targetWorkspaceId,
        SolutionAuthorityAction action,
        CancellationToken cancellationToken = default)
    {
        if (actor.SubjectKind != SolutionSubjectKind.Human
            || actor.WorkspaceId != targetWorkspaceId
            || await memberships.GetActiveAsync(
                targetWorkspaceId,
                actor.SubjectId,
                cancellationToken) is not { HasLifecycleAdministratorAuthority: true })
        {
            throw new SolutionPackageException("solutions.authorization.denied");
        }
    }
}
