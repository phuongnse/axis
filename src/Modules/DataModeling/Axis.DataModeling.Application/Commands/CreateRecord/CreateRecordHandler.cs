using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.CreateRecord;

/// <summary>US-041: Validates model exists, creates and persists the record.</summary>
public sealed class CreateRecordHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateRecordCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRecordCommand command, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.OrganizationId, cancellationToken);
        if (model is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Model not found.");

        DataRecord record = DataRecord.Create(command.ModelId, command.OrganizationId, command.Data, command.CreatedBy);

        await recordRepo.AddAsync(record, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
