using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CancelOrganizationDeletion;

public sealed class CancelOrganizationDeletionHandler(
    IOrganizationRepository orgRepo,
    IUnitOfWork uow)
    : ICommandHandler<CancelOrganizationDeletionCommand>
{
    public async Task<Result> Handle(CancelOrganizationDeletionCommand command, CancellationToken cancellationToken)
    {
        Organization? organization = await orgRepo.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        try
        {
            organization.CancelScheduledDeletion();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
