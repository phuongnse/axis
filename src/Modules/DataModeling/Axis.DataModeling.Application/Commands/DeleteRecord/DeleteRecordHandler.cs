using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.DeleteRecord;

/// <summary>US-045: Soft-deletes a record; returns 404 if already gone.</summary>
public sealed class DeleteRecordHandler(
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteRecordCommand>
{
    public async Task Handle(DeleteRecordCommand command, CancellationToken cancellationToken)
    {
        var record = await recordRepo.GetByIdAsync(
            command.RecordId, command.ModelId, command.OrganizationId, cancellationToken);

        if (record is null)
            throw new ValidationException("Record not found.");

        record.Delete();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
