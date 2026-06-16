using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.BulkDeleteRecords;

/// <summary>Validates the model exists and delegates the bulk soft-delete to the repository.</summary>
public sealed class BulkDeleteRecordsHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo)
    : ICommandHandler<BulkDeleteRecordsCommand, BulkDeleteResult>
{
    public async Task<Result<BulkDeleteResult>> Handle(
        BulkDeleteRecordsCommand command, CancellationToken cancellationToken)
    {
        if (command.RecordIds.Count == 0)
            return Result<BulkDeleteResult>.Failure(ErrorCodes.BusinessRule, "Please select at least one record.");

        DataModel? model = await modelRepo.GetByIdAsync(command.ModelId, command.TeamAccountId, cancellationToken);
        if (model is null)
            return Result<BulkDeleteResult>.Failure(ErrorCodes.NotFound, "Model not found.");

        // Deduplicate so duplicate IDs don't inflate NotFound counts or produce redundant DB work.
        IReadOnlyList<Guid> uniqueIds = command.RecordIds.Distinct().ToList().AsReadOnly();

        // BulkDeleteAsync executes immediately via a single UPDATE statement (not through EF change tracking),
        // so SaveChangesAsync is intentionally omitted here.
        int deleted = await recordRepo.BulkDeleteAsync(
            uniqueIds, command.ModelId, command.TeamAccountId, cancellationToken);

        int notFound = uniqueIds.Count - deleted;
        return Result.Success(new BulkDeleteResult(deleted, notFound));
    }
}
