using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed class GetProvisioningStatusHandler(
    IEmailVerificationTokenStore verificationTokenStore,
    IWorkspaceRegistrationTokenStore WorkspaceTokenStore,
    IUserRepository userRepo,
    IWorkspaceMembershipRepository membershipRepo,
    IWorkspaceRepository WorkspaceRepo,
    IWorkspaceModuleProvisioningRepository provisioningRepo)
    : IQueryHandler<GetProvisioningStatusQuery, ProvisioningStatusDto?>
{
    public async Task<ProvisioningStatusDto?> Handle(
        GetProvisioningStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            return null;

        string tokenHash = OpaqueTokenGenerator.Hash(query.Token.Trim());
        Guid? workspaceId = await WorkspaceTokenStore.ResolveWorkspaceIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (workspaceId is Guid resolvedworkspaceId)
            return await BuildStatusAsync(resolvedworkspaceId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return null;

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return null;

        Guid? primaryWorkspaceId = await ResolvePrimaryWorkspaceIdAsync(user.Id, cancellationToken);
        if (primaryWorkspaceId is null)
            return null;

        return await BuildStatusAsync(primaryWorkspaceId.Value, cancellationToken);
    }

    private async Task<Guid?> ResolvePrimaryWorkspaceIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkspaceMembership> memberships =
            await membershipRepo.GetByUserIdAsync(userId, cancellationToken);

        List<(WorkspaceMembership Membership, Workspace Workspace)> activeWorkspaces = [];
        foreach (WorkspaceMembership membership in memberships.Where(m => m.Status == WorkspaceMembershipStatus.Active))
        {
            Workspace? workspace = await WorkspaceRepo.GetByIdAsync(membership.workspaceId, cancellationToken);
            if (workspace is not null)
                activeWorkspaces.Add((membership, workspace));
        }

        return activeWorkspaces
            .OrderBy(item => item.Workspace.Type == WorkspaceType.Team ? 0 : 1)
            .ThenBy(item => item.Membership.CreatedAt)
            .Select(item => (Guid?)item.Workspace.Id)
            .FirstOrDefault();
    }

    private async Task<ProvisioningStatusDto?> BuildStatusAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        Workspace? Workspace = await WorkspaceRepo.GetByIdAsync(workspaceId, cancellationToken);
        if (Workspace is null)
            return null;

        IReadOnlyList<WorkspaceModuleProvisioning> modules =
            await provisioningRepo.GetAllForWorkspaceAsync(workspaceId, cancellationToken);

        bool isReady = Workspace.Status == WorkspaceStatus.Active
            && WorkspaceModuleNames.All.All(moduleName =>
                modules.Any(m =>
                    m.Module == moduleName
                    && m.Status == WorkspaceModuleProvisioningStatus.Succeeded));

        return new ProvisioningStatusDto(
            Workspace.Id,
            Workspace.Status.ToString(),
            isReady,
            modules.Select(m => new ModuleProvisioningStatusDto(
                m.Module,
                m.Status.ToString(),
                m.AttemptCount,
                m.LastError)).ToList());
    }
}
