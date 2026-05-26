using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ScheduleOrganizationDeletion;

public sealed class ScheduleOrganizationDeletionHandler(
    IOrganizationRepository orgRepo,
    IUserRepository userRepo,
    IEmailSender emailSender,
    IOrganizationDeletionScheduler deletionScheduler,
    IUnitOfWork uow)
    : ICommandHandler<ScheduleOrganizationDeletionCommand>
{
    public async Task<Result> Handle(ScheduleOrganizationDeletionCommand command, CancellationToken cancellationToken)
    {
        Organization? organization = await orgRepo.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        if (!string.Equals(command.ConfirmationName, organization.Name, StringComparison.Ordinal))
            return Result.Failure(ErrorCodes.BusinessRule, "Confirmation name must match the organization name exactly.");

        User? requester = await userRepo.GetByIdAsync(command.RequestedByUserId, command.OrganizationId, cancellationToken);
        if (requester is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        DateTime utcNow = DateTime.UtcNow;
        try
        {
            organization.ScheduleDeletion(utcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);

        await emailSender.SendOrganizationDeletionScheduledEmailAsync(
            requester.Email.Value,
            organization.Name,
            cancellationToken);

        if (organization.ScheduledHardDeleteAt is DateTime hardDeleteAt)
            await deletionScheduler.ScheduleHardDeleteAsync(organization.Id, hardDeleteAt, cancellationToken);

        return Result.Success();
    }
}
