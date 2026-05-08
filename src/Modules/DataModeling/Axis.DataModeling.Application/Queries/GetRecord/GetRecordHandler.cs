using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetRecord;

/// <summary>US-044: Returns a single record or null if not found.</summary>
public sealed class GetRecordHandler(IDataRecordRepository recordRepo)
    : IQueryHandler<GetRecordQuery, RecordDto?>
{
    public async Task<RecordDto?> Handle(GetRecordQuery query, CancellationToken cancellationToken)
    {
        var record = await recordRepo.GetByIdAsync(query.RecordId, query.ModelId, query.OrganizationId, cancellationToken);
        if (record is null) return null;

        return new RecordDto(record.Id, record.ModelId, record.CreatedAt, record.UpdatedAt, record.Data);
    }
}
