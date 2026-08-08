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

namespace Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;

public sealed class CreateBusinessObjectRecordHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IBusinessObjectDefinitionRepository definitionRepository,
    IBusinessObjectRecordRepository recordRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateBusinessObjectRecordCommand, BusinessObjectRecordDetailDto>
{
    public async Task<Result<BusinessObjectRecordDetailDto>> Handle(
        CreateBusinessObjectRecordCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectRecordFailures.MissingWorkspace<BusinessObjectRecordDetailDto>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectRecordFailures.MissingUser<BusinessObjectRecordDetailDto>();

        Result<BusinessObjectDefinitionKey> key = BusinessObjectDefinitionKey.Create(command.ObjectKey);
        if (key.IsFailure)
            return BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordDetailDto>();

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.RecordCreate,
            BusinessObjectProductActions.RecordResourceType,
            key.Value.Value,
            command.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed)
            return decision.IsUnavailable
                ? BusinessObjectRecordFailures.AuthorizationUnavailable<BusinessObjectRecordDetailDto>()
                : BusinessObjectRecordFailures.Forbidden<BusinessObjectRecordDetailDto>();

        IReadOnlyDictionary<string, IReadOnlyList<string>> values = command.Values ??
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        Result<string> payloadHash = BusinessObjectRecordPayloadHasher.Compute(values);
        if (payloadHash.IsFailure)
            return BusinessObjectRecordFailures.Invalid<BusinessObjectRecordDetailDto>(payloadHash.Error);
        BusinessObjectRecord? existing = await recordRepository.FindByIdempotencyKeyAsync(
            workspaceId,
            key.Value,
            command.IdempotencyKey.Trim(),
            cancellationToken);
        if (existing is not null)
        {
            if (decision.Scope == ProductActionScope.Own
                && existing.Owner != SubjectReferenceMapper.ToDomain(currentSubject.Subject))
                return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordDetailDto>();
            if (!StringComparer.Ordinal.Equals(existing.PayloadHash, payloadHash.Value))
                return BusinessObjectRecordFailures.IdempotencyConflict<BusinessObjectRecordDetailDto>();

            BusinessObjectDefinitionVersion? existingDefinition = await definitionRepository
                .GetPublishedVersionByIdForWorkspaceAsync(
                    existing.DefinitionVersionId,
                    workspaceId,
                    cancellationToken);
            return existingDefinition is null
                ? BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordDetailDto>()
                : BusinessObjectRecordMapper.ToDetailDto(existing, existingDefinition);
        }

        BusinessObjectDefinition? definition = await definitionRepository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        BusinessObjectDefinitionVersion? publishedVersion = definition?.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();
        if (definition is null || definition.Status != BusinessObjectDefinitionStatus.Published || publishedVersion is null)
            return BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordDetailDto>();

        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> validValues =
            BusinessObjectRecordValueValidator.ValidateAndCanonicalize(publishedVersion, values);
        if (validValues.IsFailure)
            return validValues.ErrorCode == ErrorCodes.FieldValidation && validValues.FieldErrors is not null
                ? BusinessObjectRecordFailures.Validation<BusinessObjectRecordDetailDto>(validValues.FieldErrors)
                : BusinessObjectRecordFailures.Invalid<BusinessObjectRecordDetailDto>(validValues.Error);

        Result<BusinessObjectRecord> record = BusinessObjectRecord.CreateDraft(
            workspaceId,
            publishedVersion.Id,
            publishedVersion.VersionNumber,
            publishedVersion.Key,
            command.IdempotencyKey,
            payloadHash.Value,
            validValues.Value,
            SubjectReferenceMapper.ToDomain(currentSubject.Subject),
            DateTime.UtcNow);
        if (record.IsFailure)
            return BusinessObjectRecordFailures.Invalid<BusinessObjectRecordDetailDto>(record.Error);

        await recordRepository.AddAsync(record.Value, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            BusinessObjectRecord? concurrent = await recordRepository.FindByIdempotencyKeyAsync(
                workspaceId,
                key.Value,
                command.IdempotencyKey.Trim(),
                cancellationToken);
            return concurrent is not null && StringComparer.Ordinal.Equals(concurrent.PayloadHash, payloadHash.Value)
                && (decision.Scope != ProductActionScope.Own
                    || concurrent.Owner == SubjectReferenceMapper.ToDomain(currentSubject.Subject))
                ? BusinessObjectRecordMapper.ToDetailDto(concurrent, publishedVersion)
                : BusinessObjectRecordFailures.IdempotencyConflict<BusinessObjectRecordDetailDto>();
        }

        return BusinessObjectRecordMapper.ToDetailDto(record.Value, publishedVersion);
    }
}
