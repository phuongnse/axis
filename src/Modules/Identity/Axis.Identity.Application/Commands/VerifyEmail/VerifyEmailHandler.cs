using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(
    IUserRepository userRepo,
    IUnitOfWork uow,
    ITenantSchemaProvisioner tenantProvisioner)
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

        user.VerifyEmail();
        await uow.SaveChangesAsync(cancellationToken);

        await tenantProvisioner.ProvisionAsync(user.OrganizationId, cancellationToken);

        return Result.Success();
    }
}
