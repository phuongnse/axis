using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CancelTenantDeletion;

public sealed class CancelTenantDeletionHandler(
    ITenantRepository tenantRepo,
    IUnitOfWork uow)
    : ICommandHandler<CancelTenantDeletionCommand>
{
    public async Task<Result> Handle(CancelTenantDeletionCommand command, CancellationToken cancellationToken)
    {
        Tenant? Tenant = await tenantRepo.GetByIdAsync(command.tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure(ErrorCodes.NotFound, "Tenant not found.");

        try
        {
            Tenant.CancelScheduledDeletion();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
