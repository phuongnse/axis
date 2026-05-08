using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Queries.GetRecords;

/// <summary>US-042/043: Returns a paginated page of records; validates model exists first.</summary>
public sealed class GetRecordsHandler(
    IDataModelRepository modelRepo,
    IDataRecordRepository recordRepo)
    : IQueryHandler<GetRecordsQuery, RecordsPageDto>
{
    public async Task<RecordsPageDto> Handle(GetRecordsQuery query, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(query.ModelId, query.OrganizationId, cancellationToken);
        if (model is null)
            throw new ValidationException("Model not found.");

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (records, total) = await recordRepo.GetPagedAsync(
            query.ModelId, query.OrganizationId,
            page, pageSize,
            query.Search,
            cancellationToken);

        return new RecordsPageDto(
            records.Select(r => new RecordDto(r.Id, r.ModelId, r.CreatedAt, r.UpdatedAt, r.Data)).ToList().AsReadOnly(),
            total,
            page,
            pageSize,
            (int)Math.Ceiling(total / (double)pageSize));
    }
}
