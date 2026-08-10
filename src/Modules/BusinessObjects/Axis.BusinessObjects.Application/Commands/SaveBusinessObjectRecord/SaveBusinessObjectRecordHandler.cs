using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Commands.SaveBusinessObjectRecord;

public sealed class SaveBusinessObjectRecordHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IBusinessObjectRecordRepository recordRepository,
    IBusinessObjectDefinitionRepository definitionRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SaveBusinessObjectRecordCommand, BusinessObjectRecordDetailDto>
{
    public async Task<Result<BusinessObjectRecordDetailDto>> Handle(
        SaveBusinessObjectRecordCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectRecordFailures.MissingWorkspace<BusinessObjectRecordDetailDto>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectRecordFailures.MissingUser<BusinessObjectRecordDetailDto>();

        BusinessObjectRecord? record = await recordRepository.GetByIdForWorkspaceAsync(
            BusinessObjectRecordId.From(command.RecordId),
            workspaceId,
            cancellationToken);
        if (record is null)
            return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordDetailDto>();

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.RecordSave,
            BusinessObjectProductActions.RecordResourceType,
            record.ObjectKey.Value,
            command.CorrelationId,
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
        if (definition is null)
            return BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordDetailDto>();

        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> validValues =
            BusinessObjectRecordValueValidator.ValidateAndCanonicalize(definition, command.Values);
        if (validValues.IsFailure)
            return validValues.ErrorCode == ErrorCodes.FieldValidation && validValues.FieldErrors is not null
                ? BusinessObjectRecordFailures.Validation<BusinessObjectRecordDetailDto>(validValues.FieldErrors)
                : BusinessObjectRecordFailures.Invalid<BusinessObjectRecordDetailDto>(validValues.Error);

        Result saved = record.SaveDraft(
            command.ExpectedRevision,
            validValues.Value,
            SubjectReferenceMapper.ToDomain(currentSubject.Subject),
            DateTime.UtcNow);
        if (saved.IsFailure)
            return saved.ErrorCode == ErrorCodes.Conflict
                ? BusinessObjectRecordFailures.Conflict<BusinessObjectRecordDetailDto>(saved.Error)
                : BusinessObjectRecordFailures.Invalid<BusinessObjectRecordDetailDto>(saved.Error);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return BusinessObjectRecordFailures.Conflict<BusinessObjectRecordDetailDto>(
                "The record has changed.");
        }

        return BusinessObjectRecordMapper.ToDetailDto(record, definition);
    }
}
