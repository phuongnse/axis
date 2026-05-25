using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(
    IUserRepository userRepo,
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo,
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Result> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(command.Token, out Guid userId))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link.");

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

        // Admin role is assigned at registration; ensure it remains after verify (US-003).
        Role? adminRole = await roleRepo.GetByNameAsync("Admin", organization.Id, cancellationToken);
        if (adminRole is not null && !user.RoleIds.Contains(adminRole.Id))
            user.AssignRole(adminRole.Id);

        // VerifyEmail raises OrganizationVerified; modules provision asynchronously (ADR-019).
        user.VerifyEmail();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
