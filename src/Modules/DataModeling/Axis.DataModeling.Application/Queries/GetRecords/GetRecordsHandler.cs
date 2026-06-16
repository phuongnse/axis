using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetRecords;

/// <summary>/043: Returns a paginated page of records; validates model exists first.</summary>
public sealed class GetRecordsHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo)
    : IQueryHandler<GetRecordsQuery, Result<RecordsPageDto>>
{
    public async Task<Result<RecordsPageDto>> Handle(GetRecordsQuery query, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(query.ModelId, query.tenantId, cancellationToken);
        if (model is null)
            return Result.Failure<RecordsPageDto>(ErrorCodes.NotFound, "Model not found.");

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<DataRecord> records, int total) = await recordRepo.GetPagedAsync(
            query.ModelId, query.tenantId,
            page, pageSize,
            query.Search,
            query.Filters,
            query.SortBy,
            query.SortDir,
            cancellationToken);

        return new RecordsPageDto(
            records.Select(r => new RecordDto(r.Id, r.ModelId, r.CreatedAt, r.UpdatedAt, r.Data)).ToList().AsReadOnly(),
            total,
            page,
            pageSize,
            (int)Math.Ceiling(total / (double)pageSize));
    }
}
