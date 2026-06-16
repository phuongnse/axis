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
    IOrganizationRegistrationTokenStore organizationTokenStore,
    IUserRepository userRepo,
    IOrganizationMembershipRepository membershipRepo,
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand, VerifyEmailSuccessDto>
{
    private static readonly TimeSpan FirstUserSetupTokenLifetime = TimeSpan.FromHours(24);

    public async Task<Result<VerifyEmailSuccessDto>> Handle(
        VerifyEmailCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        EmailVerificationTokenResolveResult resolved =
            await tokenStore.ResolveForVerificationAsync(tokenHash, cancellationToken);

        return resolved.State switch
        {
            EmailVerificationTokenState.NotFound =>
                await VerifyOrganizationContactAsync(tokenHash, cancellationToken),
            EmailVerificationTokenState.Expired =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "This verification link has expired. Please request a new verification email."),
            EmailVerificationTokenState.AlreadyUsed =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "This link has already been used. Please sign in."),
            EmailVerificationTokenState.Valid => await VerifyUserAsync(
                resolved.UserId!.Value,
                cancellationToken),
            _ => Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link."),
        };
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyOrganizationContactAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        Result<Guid> resolved =
            await organizationTokenStore.ResolveVerificationAsync(tokenHash, cancellationToken);

        return resolved.IsFailure
            ? Result.Failure<VerifyEmailSuccessDto>(
                resolved.ErrorCode ?? ErrorCodes.BusinessRule,
                resolved.Error)
            : await VerifyOrganizationAsync(resolved.Value, cancellationToken);
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        Organization? organization = await organizationRepo.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (organization.Status != OrganizationStatus.PendingVerification)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");
        }

        organization.BeginProvisioningAfterContactVerification();

        List<TenantModuleProvisioning> pendingModules = TenantModuleNames.All
            .Select(module => TenantModuleProvisioning.CreatePending(organization.Id, module))
            .ToList();
        await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);

        (string rawSetupToken, string setupTokenHash) = OpaqueTokenGenerator.Create();
        await organizationTokenStore.CreateFirstUserSetupAsync(
            organization.Id,
            setupTokenHash,
            DateTime.UtcNow.Add(FirstUserSetupTokenLifetime),
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            null,
            organization.Id,
            organization.OwnerEmail.Value,
            organization.Name,
            [],
            VerifyEmailNextStep.RegisterUser,
            rawSetupToken));
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (user.IsEmailVerified)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");
        }

        OrganizationMembership? membership =
            await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);
        if (membership is null)
        {
            user.VerifyEmail();
            await uow.SaveChangesAsync(cancellationToken);

            return Result.Success(new VerifyEmailSuccessDto(
                user.Id,
                null,
                user.Email.Value,
                user.FullName,
                [],
                VerifyEmailNextStep.Dashboard));
        }

        Organization? organization = await organizationRepo.GetByIdAsync(membership.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (organization.Status == OrganizationStatus.PendingVerification)
        {
            organization.BeginProvisioningAfterOwnerVerification();

            List<TenantModuleProvisioning> pendingModules = TenantModuleNames.All
                .Select(module => TenantModuleProvisioning.CreatePending(organization.Id, module))
                .ToList();
            await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);
        }
        else if (!organization.AllowsSignIn())
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "Organization is not ready for sign-in.");
        }

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", organization.Id, cancellationToken);
        if (adminRole is not null && !membership.RoleIds.Contains(adminRole.Id))
            membership.AssignRole(adminRole.Id);

        user.VerifyEmail();

        // Gather the sign-in claims here so the endpoint can establish the session
        // from a single command result — no second round-trip that could fail after
        // the one-time link has already been consumed. Read before commit so any
        // failure leaves the verification token unused.
        IReadOnlyList<Role> roles = await roleRepo.GetByIdsAsync(membership.RoleIds, organization.Id, cancellationToken);
        List<string> permissions = roles
            .SelectMany(role => role.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            user.Id,
            organization.Id,
            user.Email.Value,
            user.FullName,
            permissions,
            VerifyEmailNextStep.WorkspaceProvisioning));
    }
}
