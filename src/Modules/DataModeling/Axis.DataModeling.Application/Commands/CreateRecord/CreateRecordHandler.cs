using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.CreateRecord;

/// <summary>/035: Validates model exists, validates field data, creates and persists the record.</summary>
public sealed class CreateRecordHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateRecordCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRecordCommand command, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.tenantId, cancellationToken);
        if (model is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Model not found.");

        Dictionary<string, string[]> fieldErrors = RecordFieldValidator.Validate(command.Data, model.Fields);
        if (fieldErrors.Count > 0)
            return Result.FieldValidation<Guid>(fieldErrors);

        DataRecord record = DataRecord.Create(command.ModelId, command.tenantId, command.Data, command.CreatedBy);

        await recordRepo.AddAsync(record, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
