using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.CreateRecord;

/// <summary>US-041: Validates model exists, creates and persists the record.</summary>
public sealed class CreateRecordHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo,
    IUnitOfWork uow,
    IRecordValidator validator)
    : ICommandHandler<CreateRecordCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRecordCommand command, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Model not found.");

        var validationErrors = await validator.ValidateAsync(model, command.Data, cancellationToken);
        if (validationErrors.Count > 0)
            throw new ValidationException(validationErrors);

        DataRecord record = DataRecord.Create(command.ModelId, command.OrganizationId, command.Data, command.CreatedBy);

        await recordRepo.AddAsync(record, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
