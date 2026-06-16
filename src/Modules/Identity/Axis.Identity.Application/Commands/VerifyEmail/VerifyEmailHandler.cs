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
    ITenantRegistrationTokenStore TenantTokenStore,
    IUserRepository userRepo,
    ITenantMembershipRepository membershipRepo,
    ITenantRepository TenantRepo,
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
                await VerifyTenantContactAsync(tokenHash, cancellationToken),
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

    private async Task<Result<VerifyEmailSuccessDto>> VerifyTenantContactAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        Result<Guid> resolved =
            await TenantTokenStore.ResolveVerificationAsync(tokenHash, cancellationToken);

        return resolved.IsFailure
            ? Result.Failure<VerifyEmailSuccessDto>(
                resolved.ErrorCode ?? ErrorCodes.BusinessRule,
                resolved.Error)
            : await VerifyTenantAsync(resolved.Value, cancellationToken);
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Tenant? Tenant = await TenantRepo.GetByIdAsync(tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (Tenant.Status != TenantStatus.PendingVerification)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");
        }

        Tenant.BeginProvisioningAfterContactVerification();

        List<TenantModuleProvisioning> pendingModules = TenantModuleNames.All
            .Select(module => TenantModuleProvisioning.CreatePending(Tenant.Id, module))
            .ToList();
        await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);

        (string rawSetupToken, string setupTokenHash) = OpaqueTokenGenerator.Create();
        await TenantTokenStore.CreateFirstUserSetupAsync(
            Tenant.Id,
            setupTokenHash,
            DateTime.UtcNow.Add(FirstUserSetupTokenLifetime),
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            null,
            Tenant.Id,
            Tenant.OwnerEmail.Value,
            Tenant.Name,
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

        TenantMembership? membership =
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

        Tenant? Tenant = await TenantRepo.GetByIdAsync(membership.tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (Tenant.Status == TenantStatus.PendingVerification)
        {
            Tenant.BeginProvisioningAfterOwnerVerification();

            List<TenantModuleProvisioning> pendingModules = TenantModuleNames.All
                .Select(module => TenantModuleProvisioning.CreatePending(Tenant.Id, module))
                .ToList();
            await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);
        }
        else if (!Tenant.AllowsSignIn())
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "Tenant is not ready for sign-in.");
        }

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", Tenant.Id, cancellationToken);
        if (adminRole is not null && !membership.RoleIds.Contains(adminRole.Id))
            membership.AssignRole(adminRole.Id);

        user.VerifyEmail();

        // Gather the sign-in claims here so the endpoint can establish the session
        // from a single command result — no second round-trip that could fail after
        // the one-time link has already been consumed. Read before commit so any
        // failure leaves the verification token unused.
        IReadOnlyList<Role> roles = await roleRepo.GetByIdsAsync(membership.RoleIds, Tenant.Id, cancellationToken);
        List<string> permissions = roles
            .SelectMany(role => role.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            user.Id,
            Tenant.Id,
            user.Email.Value,
            user.FullName,
            permissions,
            VerifyEmailNextStep.WorkspaceProvisioning));
    }
}
