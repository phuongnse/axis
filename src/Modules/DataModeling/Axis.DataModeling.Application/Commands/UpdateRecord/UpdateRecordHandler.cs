using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.UpdateRecord;

/// <summary>US-044: Loads the record, replaces its data, and saves.</summary>
public sealed class UpdateRecordHandler(
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateRecordCommand>
{
    public async Task Handle(UpdateRecordCommand command, CancellationToken cancellationToken)
    {
        var record = await recordRepo.GetByIdAsync(
            command.RecordId, command.ModelId, command.OrganizationId, cancellationToken);

        if (record is null)
            throw new ValidationException("Record not found.");

        record.Update(command.Data);
        await uow.SaveChangesAsync(cancellationToken);
    }
}
