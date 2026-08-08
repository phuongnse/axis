using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using DomainSubjectReference = Axis.BusinessObjects.Domain.ValueObjects.SubjectReference;

namespace Axis.BusinessObjects.Application.Queries.ListBusinessObjectRecords;

public sealed class ListBusinessObjectRecordsHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IBusinessObjectRecordRepository repository)
    : IQueryHandler<ListBusinessObjectRecordsQuery, Result<PagedResult<BusinessObjectRecordListItemDto>>>
{
    public async Task<Result<PagedResult<BusinessObjectRecordListItemDto>>> Handle(
        ListBusinessObjectRecordsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectRecordFailures.MissingWorkspace<PagedResult<BusinessObjectRecordListItemDto>>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectRecordFailures.MissingUser<PagedResult<BusinessObjectRecordListItemDto>>();

        BusinessObjectDefinitionKey? objectKey = null;
        if (!string.IsNullOrWhiteSpace(query.ObjectKey))
        {
            Result<BusinessObjectDefinitionKey> parsed = BusinessObjectDefinitionKey.Create(query.ObjectKey);
            if (parsed.IsFailure)
                return BusinessObjectRecordFailures.Invalid<PagedResult<BusinessObjectRecordListItemDto>>(parsed.Error);
            objectKey = parsed.Value;
        }

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.RecordList,
            BusinessObjectProductActions.RecordResourceType,
            objectKey?.Value,
            query.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed)
            return decision.IsUnavailable
                ? BusinessObjectRecordFailures.AuthorizationUnavailable<PagedResult<BusinessObjectRecordListItemDto>>()
                : BusinessObjectRecordFailures.Forbidden<PagedResult<BusinessObjectRecordListItemDto>>();

        DomainSubjectReference owner =
            SubjectReferenceMapper.ToDomain(currentSubject.Subject);
        int totalCount = decision.Scope == ProductActionScope.Own
            ? await repository.CountOwnedForWorkspaceAsync(workspaceId, owner, objectKey, cancellationToken)
            : await repository.CountForWorkspaceAsync(workspaceId, objectKey, cancellationToken);
        IReadOnlyList<BusinessObjectRecord> records = decision.Scope == ProductActionScope.Own
            ? await repository.ListOwnedForWorkspaceAsync(
                workspaceId,
                owner,
                objectKey,
                query.Page,
                query.PageSize,
                cancellationToken)
            : await repository.ListForWorkspaceAsync(
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
