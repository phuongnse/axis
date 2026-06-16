using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ScheduleTenantDeletion;

public sealed class ScheduleTenantDeletionHandler(
    ITenantRepository tenantRepo,
    IUserRepository userRepo,
    IEmailSender emailSender,
    ITenantDeletionScheduler deletionScheduler,
    IUnitOfWork uow)
    : ICommandHandler<ScheduleTenantDeletionCommand>
{
    public async Task<Result> Handle(ScheduleTenantDeletionCommand command, CancellationToken cancellationToken)
    {
        Tenant? Tenant = await tenantRepo.GetByIdAsync(command.tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure(ErrorCodes.NotFound, "Tenant not found.");

        if (!string.Equals(command.ConfirmationName, Tenant.Name, StringComparison.Ordinal))
            return Result.Failure(ErrorCodes.BusinessRule, "Confirmation name must match the Tenant name exactly.");

        User? requester = await userRepo.GetByIdAsync(command.RequestedByUserId, command.tenantId, cancellationToken);
        if (requester is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        DateTime utcNow = DateTime.UtcNow;
        try
        {
            Tenant.ScheduleDeletion(utcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);

        if (Tenant.ScheduledHardDeleteAt is not DateTime hardDeleteAt)
            return Result.Failure(ErrorCodes.BusinessRule, "Deletion schedule was not created.");

        try
        {
            await deletionScheduler.ScheduleHardDeleteAsync(Tenant.Id, hardDeleteAt, cancellationToken);
        }
        catch (Exception)
        {
            try
            {
                Tenant.CancelScheduledDeletion();
                await uow.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Tenant state could not be rolled back — surface queue failure to caller.
            }

            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Failed to queue Tenant deletion. The Tenant was not scheduled for deletion.");
        }

        await emailSender.SendTenantDeletionScheduledEmailAsync(
            requester.Email.Value,
            Tenant.Name,
            cancellationToken);

        return Result.Success();
    }
}
