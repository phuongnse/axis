using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.UpdateRecord;

/// <summary>US-044: Loads the record, replaces its data, and saves.</summary>
public sealed class UpdateRecordHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo,
    IUnitOfWork uow,
    IRecordValidator validator)
    : ICommandHandler<UpdateRecordCommand>
{
    public async Task<Result> Handle(UpdateRecordCommand command, CancellationToken cancellationToken)
    {
        DataRecord? record = await recordRepo.GetByIdAsync(
            command.RecordId, command.ModelId, command.OrganizationId, cancellationToken);

        if (record is null)
            return Result.Failure(ErrorCodes.NotFound, "Record not found.");

        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        var validationErrors = await validator.ValidateAsync(model, command.Data, cancellationToken);
        if (validationErrors.Count > 0)
            throw new ValidationException(validationErrors);

        record.Update(command.Data);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
