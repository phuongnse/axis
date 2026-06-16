using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RetryWorkspaceProvisioning;

public sealed record RetryWorkspaceProvisioningCommand(string Token) : ICommand;

public sealed class RetryWorkspaceProvisioningHandler(
    IEmailVerificationTokenStore verificationTokenStore,
    IWorkspaceRegistrationTokenStore WorkspaceTokenStore,
    IUserRepository userRepo,
    IWorkspaceMembershipRepository membershipRepo,
    IWorkspaceRepository WorkspaceRepo,
    IWorkspaceModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryWorkspaceProvisioningCommand>
{
    public async Task<Result> Handle(RetryWorkspaceProvisioningCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification token.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        Guid? workspaceId = await WorkspaceTokenStore.ResolveWorkspaceIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (workspaceId is Guid resolvedworkspaceId)
            return await RetryForWorkspaceAsync(resolvedworkspaceId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return Result.Failure(ErrorCodes.NotFound, "Verification token not found.");

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return Result.Failure(ErrorCodes.BusinessRule, "Account is not ready for provisioning retry.");

        Guid? primaryWorkspaceId = await ResolvePrimaryWorkspaceIdAsync(user.Id, cancellationToken);
        if (primaryWorkspaceId is null)
            return Result.Failure(ErrorCodes.NotFound, "Workspace not found.");

        return await RetryForWorkspaceAsync(primaryWorkspaceId.Value, cancellationToken);
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

    private async Task<Result> RetryForWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        Workspace? Workspace = await WorkspaceRepo.GetByIdAsync(workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure(ErrorCodes.NotFound, "Workspace not found.");

        if (Workspace.Status != WorkspaceStatus.ProvisioningFailed)
            return Result.Success();

        IReadOnlyList<WorkspaceModuleProvisioning> modules =
            await provisioningRepo.GetAllForWorkspaceAsync(workspaceId, cancellationToken);

        foreach (WorkspaceModuleProvisioning module in modules)
        {
            if (module.Status == WorkspaceModuleProvisioningStatus.Failed)
                module.ResetForManualRetry();
        }

        Workspace.RetryProvisioning();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
