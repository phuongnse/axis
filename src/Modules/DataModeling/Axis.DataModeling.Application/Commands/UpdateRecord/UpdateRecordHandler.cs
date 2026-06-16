using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.UpdateRecord;

/// <summary>/035: Loads the record, validates field data, replaces its data, and saves.</summary>
public sealed class UpdateRecordHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<UpdateRecordCommand>
{
    public async Task<Result> Handle(UpdateRecordCommand command, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.TeamAccountId, cancellationToken);
        if (model is null)
            return Result.Failure(ErrorCodes.NotFound, "Model not found.");

        DataRecord? record = await recordRepo.GetByIdAsync(
            command.RecordId, command.ModelId, command.TeamAccountId, cancellationToken);
        if (record is null)
            return Result.Failure(ErrorCodes.NotFound, "Record not found.");

        Dictionary<string, string[]> fieldErrors = RecordFieldValidator.Validate(command.Data, model.Fields);
        if (fieldErrors.Count > 0)
            return Result.FieldValidation(fieldErrors);

        record.Update(command.Data);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
