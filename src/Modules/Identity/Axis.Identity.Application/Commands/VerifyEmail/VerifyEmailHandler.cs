using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(
    IEmailVerificationTokenStore tokenStore,
    IUserRepository userRepo,
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Result> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        EmailVerificationTokenResolveResult resolved =
            await tokenStore.ResolveForVerificationAsync(tokenHash, cancellationToken);

        return resolved.State switch
        {
            EmailVerificationTokenState.NotFound =>
                Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link."),
            EmailVerificationTokenState.Expired =>
                Result.Failure(
                    ErrorCodes.BusinessRule,
                    "This verification link has expired. Please request a new verification email."),
            EmailVerificationTokenState.AlreadyUsed =>
                Result.Failure(
                    ErrorCodes.BusinessRule,
                    "This link has already been used. Please sign in."),
            EmailVerificationTokenState.Valid => await VerifyUserAsync(
                resolved.UserId!.Value,
                tokenHash,
                cancellationToken),
            _ => Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link."),
        };
    }

    private async Task<Result> VerifyUserAsync(
        Guid userId,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (user.IsEmailVerified)
            return Result.Failure(ErrorCodes.BusinessRule, "This link has already been used. Please sign in.");

        Organization? organization = await organizationRepo.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link.");

        organization.BeginProvisioning();

        List<TenantModuleProvisioning> pendingModules = TenantModuleNames.All
            .Select(module => TenantModuleProvisioning.CreatePending(organization.Id, module))
            .ToList();
        await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", organization.Id, cancellationToken);
        if (adminRole is not null && !user.RoleIds.Contains(adminRole.Id))
            user.AssignRole(adminRole.Id);

        user.VerifyEmail();
        await uow.SaveChangesAsync(cancellationToken);
        await tokenStore.InvalidateAsync(tokenHash, cancellationToken);

        return Result.Success();
    }
}
