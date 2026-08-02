using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;

public sealed class GetBusinessObjectRecordHandler(
    ICurrentUser currentUser,
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

        BusinessObjectRecord? record = await recordRepository.GetByIdForWorkspaceAsync(
            BusinessObjectRecordId.From(query.RecordId),
            workspaceId,
            cancellationToken);
        if (record is null)
            return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordDetailDto>();
        BusinessObjectDefinitionVersion? definition = await definitionRepository
            .GetPublishedVersionByIdForWorkspaceAsync(record.DefinitionVersionId, workspaceId, cancellationToken);
        return definition is null
            ? BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordDetailDto>()
            : BusinessObjectRecordMapper.ToDetailDto(record, definition);
    }
}
