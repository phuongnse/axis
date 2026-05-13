using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.DeleteRecord;

/// <summary>US-045: Soft-deletes a record; returns 404 if already gone.</summary>
public sealed class DeleteRecordHandler(
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteRecordCommand>
{
    public async Task<Result> Handle(DeleteRecordCommand command, CancellationToken cancellationToken)
    {
        DataRecord? record = await recordRepo.GetByIdAsync(
            command.RecordId, command.ModelId, command.OrganizationId, cancellationToken);

        if (record is null)
            return Result.Failure(ErrorCodes.NotFound, "Record not found.");

        record.Delete();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
