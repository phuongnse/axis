using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.BulkDeleteRecords;

public sealed class BulkDeleteRecordsHandler(
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<BulkDeleteRecordsCommand>
{
    public async Task<Result> Handle(BulkDeleteRecordsCommand command, CancellationToken cancellationToken)
    {
        if (command.RecordIds.Count == 0)
            return Result.Failure("validation_error", "No records selected for deletion.");

        foreach (Guid id in command.RecordIds)
        {
            DataRecord? record = await recordRepo.GetByIdAsync(id, command.ModelId, command.OrganizationId, cancellationToken);
            record?.Delete();
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
