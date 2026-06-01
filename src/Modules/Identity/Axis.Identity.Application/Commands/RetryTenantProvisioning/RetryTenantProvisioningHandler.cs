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
    IUserRepository userRepo,
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryTenantProvisioningCommand>
{
    public async Task<Result> Handle(RetryTenantProvisioningCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification token.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return Result.Failure(ErrorCodes.NotFound, "Verification token not found.");

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return Result.Failure(ErrorCodes.BusinessRule, "Account is not ready for provisioning retry.");

        Organization? organization = await organizationRepo.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        if (organization.Status != OrganizationStatus.ProvisioningFailed)
            return Result.Success();

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForOrganizationAsync(user.OrganizationId, cancellationToken);

        foreach (TenantModuleProvisioning module in modules)
        {
            if (module.Status == TenantModuleProvisioningStatus.Failed)
                module.ResetForManualRetry();
        }

        organization.RetryProvisioning();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
