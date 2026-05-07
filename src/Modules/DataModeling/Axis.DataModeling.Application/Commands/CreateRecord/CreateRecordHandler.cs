using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.CreateRecord;

/// <summary>US-041: Validates model exists, creates and persists the record.</summary>
public sealed class CreateRecordHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateRecordCommand command, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        var record = DataRecord.Create(command.ModelId, command.OrganizationId, command.Data);

        await recordRepo.AddAsync(record, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
