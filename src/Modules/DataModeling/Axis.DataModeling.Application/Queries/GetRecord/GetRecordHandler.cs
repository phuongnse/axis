using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetRecord;

/// <summary>Returns a single record by ID; 404 if not found.</summary>
public sealed class GetRecordHandler(IDataRecordRepository recordRepo)
    : IQueryHandler<GetRecordQuery, Result<RecordDto>>
{
    public async Task<Result<RecordDto>> Handle(GetRecordQuery query, CancellationToken cancellationToken)
    {
        DataRecord? record = await recordRepo.GetByIdAsync(query.RecordId, query.ModelId, query.TeamAccountId, cancellationToken);
        if (record is null)
            return Result.Failure<RecordDto>(ErrorCodes.NotFound, "Record not found.");

        return new RecordDto(record.Id, record.ModelId, record.CreatedAt, record.UpdatedAt, record.Data);
    }
}
