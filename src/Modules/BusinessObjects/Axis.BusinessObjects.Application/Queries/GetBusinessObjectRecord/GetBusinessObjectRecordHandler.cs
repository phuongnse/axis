using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;

public sealed class GetBusinessObjectRecordHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IBusinessObjectRecordRepository recordRepository,
    IBusinessObjectDefinitionRepository definitionRepository)
    : IQueryHandler<GetBusinessObjectRecordQuery, Result<BusinessObjectRecordDetailDto>>
{
    public async Task<Result<BusinessObjectRecordDetailDto>> Handle(
        GetBusinessObjectRecordQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectRecordFailures.MissingWorkspace<BusinessObjectRecordDetailDto>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectRecordFailures.MissingUser<BusinessObjectRecordDetailDto>();

        BusinessObjectRecord? record = await recordRepository.GetByIdForWorkspaceAsync(
            BusinessObjectRecordId.From(query.RecordId),
            workspaceId,
            cancellationToken);
        if (record is null)
            return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordDetailDto>();

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.RecordRead,
            BusinessObjectProductActions.RecordResourceType,
            record.ObjectKey.Value,
            query.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed)
            return decision.IsUnavailable
                ? BusinessObjectRecordFailures.AuthorizationUnavailable<BusinessObjectRecordDetailDto>()
                : BusinessObjectRecordFailures.Forbidden<BusinessObjectRecordDetailDto>();
        if (decision.Scope == ProductActionScope.Own
            && record.Owner != SubjectReferenceMapper.ToDomain(currentSubject.Subject))
            return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordDetailDto>();
        BusinessObjectDefinitionVersion? definition = await definitionRepository
            .GetPublishedVersionByIdForWorkspaceAsync(record.DefinitionVersionId, workspaceId, cancellationToken);
        return definition is null
            ? BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordDetailDto>()
            : BusinessObjectRecordMapper.ToDetailDto(record, definition);
    }
}
