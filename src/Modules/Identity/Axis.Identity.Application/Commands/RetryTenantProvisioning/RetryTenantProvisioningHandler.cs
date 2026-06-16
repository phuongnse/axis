using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RetryTenantProvisioning;

public sealed record RetryTenantProvisioningCommand(string Token) : ICommand;

public sealed class RetryTenantProvisioningHandler(
    IEmailVerificationTokenStore verificationTokenStore,
    ITeamAccountRegistrationTokenStore teamAccountTokenStore,
    IUserRepository userRepo,
    ITeamAccountMembershipRepository membershipRepo,
    ITeamAccountRepository teamAccountRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryTenantProvisioningCommand>
{
    public async Task<Result> Handle(RetryTenantProvisioningCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification token.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        Guid? teamAccountId = await teamAccountTokenStore.ResolveTeamAccountIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (teamAccountId is Guid resolvedTeamAccountId)
            return await RetryForTeamAccountAsync(resolvedTeamAccountId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return Result.Failure(ErrorCodes.NotFound, "Verification token not found.");

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return Result.Failure(ErrorCodes.BusinessRule, "Account is not ready for provisioning retry.");

        TeamAccountMembership? membership =
            await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);
        if (membership is null)
            return Result.Failure(ErrorCodes.NotFound, "Team account not found.");

        return await RetryForTeamAccountAsync(membership.TeamAccountId, cancellationToken);
    }

    private async Task<Result> RetryForTeamAccountAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken)
    {
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null)
            return Result.Failure(ErrorCodes.NotFound, "Team account not found.");

        if (teamAccount.Status != TeamAccountStatus.ProvisioningFailed)
            return Result.Success();

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForTeamAccountAsync(teamAccountId, cancellationToken);

        foreach (TenantModuleProvisioning module in modules)
        {
            if (module.Status == TenantModuleProvisioningStatus.Failed)
                module.ResetForManualRetry();
        }

        teamAccount.RetryProvisioning();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
