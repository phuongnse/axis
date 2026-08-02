using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectRecords;

public sealed class ListBusinessObjectRecordsHandler(
    ICurrentUser currentUser,
    IBusinessObjectRecordRepository repository)
    : IQueryHandler<ListBusinessObjectRecordsQuery, Result<PagedResult<BusinessObjectRecordListItemDto>>>
{
    public async Task<Result<PagedResult<BusinessObjectRecordListItemDto>>> Handle(
        ListBusinessObjectRecordsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectRecordFailures.MissingWorkspace<PagedResult<BusinessObjectRecordListItemDto>>();

        BusinessObjectDefinitionKey? objectKey = null;
        if (!string.IsNullOrWhiteSpace(query.ObjectKey))
        {
            Result<BusinessObjectDefinitionKey> parsed = BusinessObjectDefinitionKey.Create(query.ObjectKey);
            if (parsed.IsFailure)
                return BusinessObjectRecordFailures.Invalid<PagedResult<BusinessObjectRecordListItemDto>>(parsed.Error);
            objectKey = parsed.Value;
        }

        int totalCount = await repository.CountForWorkspaceAsync(
            workspaceId,
            objectKey,
            cancellationToken);
        IReadOnlyList<BusinessObjectRecord> records = await repository.ListForWorkspaceAsync(
            workspaceId,
            objectKey,
            query.Page,
            query.PageSize,
            cancellationToken);

        return new PagedResult<BusinessObjectRecordListItemDto>(
            records.Select(BusinessObjectRecordMapper.ToListItemDto).ToArray(),
            totalCount,
            query.Page,
            query.PageSize);
    }
}
