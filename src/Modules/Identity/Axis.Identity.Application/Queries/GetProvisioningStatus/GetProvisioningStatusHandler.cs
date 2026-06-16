using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed class GetProvisioningStatusHandler(
    IEmailVerificationTokenStore verificationTokenStore,
    ITeamAccountRegistrationTokenStore teamAccountTokenStore,
    IUserRepository userRepo,
    ITeamAccountMembershipRepository membershipRepo,
    ITeamAccountRepository teamAccountRepo,
    ITenantModuleProvisioningRepository provisioningRepo)
    : IQueryHandler<GetProvisioningStatusQuery, ProvisioningStatusDto?>
{
    public async Task<ProvisioningStatusDto?> Handle(
        GetProvisioningStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            return null;

        string tokenHash = OpaqueTokenGenerator.Hash(query.Token.Trim());
        Guid? teamAccountId = await teamAccountTokenStore.ResolveTeamAccountIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (teamAccountId is Guid resolvedTeamAccountId)
            return await BuildStatusAsync(resolvedTeamAccountId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return null;

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return null;

        TeamAccountMembership? membership =
            await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);
        if (membership is null)
            return null;

        return await BuildStatusAsync(membership.TeamAccountId, cancellationToken);
    }

    private async Task<ProvisioningStatusDto?> BuildStatusAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken)
    {
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null)
            return null;

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForTeamAccountAsync(teamAccountId, cancellationToken);

        bool isReady = teamAccount.Status == TeamAccountStatus.Active
            && TenantModuleNames.All.All(moduleName =>
                modules.Any(m =>
                    m.Module == moduleName
                    && m.Status == TenantModuleProvisioningStatus.Succeeded));

        return new ProvisioningStatusDto(
            teamAccount.Id,
            teamAccount.Status.ToString(),
            isReady,
            modules.Select(m => new ModuleProvisioningStatusDto(
                m.Module,
                m.Status.ToString(),
                m.AttemptCount,
                m.LastError)).ToList());
    }
}
