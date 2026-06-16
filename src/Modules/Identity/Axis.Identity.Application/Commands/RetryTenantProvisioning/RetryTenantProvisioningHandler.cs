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
    ITenantRegistrationTokenStore TenantTokenStore,
    IUserRepository userRepo,
    ITenantMembershipRepository membershipRepo,
    ITenantRepository TenantRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryTenantProvisioningCommand>
{
    public async Task<Result> Handle(RetryTenantProvisioningCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification token.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        Guid? tenantId = await TenantTokenStore.ResolvetenantIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (tenantId is Guid resolvedtenantId)
            return await RetryForTenantAsync(resolvedtenantId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return Result.Failure(ErrorCodes.NotFound, "Verification token not found.");

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return Result.Failure(ErrorCodes.BusinessRule, "Account is not ready for provisioning retry.");

        TenantMembership? membership =
            await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);
        if (membership is null)
            return Result.Failure(ErrorCodes.NotFound, "Tenant not found.");

        return await RetryForTenantAsync(membership.tenantId, cancellationToken);
    }

    private async Task<Result> RetryForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Tenant? Tenant = await TenantRepo.GetByIdAsync(tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure(ErrorCodes.NotFound, "Tenant not found.");

        if (Tenant.Status != TenantStatus.ProvisioningFailed)
            return Result.Success();

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForTenantAsync(tenantId, cancellationToken);

        foreach (TenantModuleProvisioning module in modules)
        {
            if (module.Status == TenantModuleProvisioningStatus.Failed)
                module.ResetForManualRetry();
        }

        Tenant.RetryProvisioning();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
