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

namespace Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;

public sealed class SubmitBusinessObjectRecordHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IBusinessObjectRecordRepository recordRepository,
    IBusinessObjectDefinitionRepository definitionRepository,
    BusinessObjectRecordRuleEvaluator ruleEvaluator,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SubmitBusinessObjectRecordCommand, BusinessObjectRecordSubmitResultDto>
{
    public async Task<Result<BusinessObjectRecordSubmitResultDto>> Handle(
        SubmitBusinessObjectRecordCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectRecordFailures.MissingWorkspace<BusinessObjectRecordSubmitResultDto>();
        if (currentSubject.Subject.Id == Guid.Empty || !Enum.IsDefined(currentSubject.Subject.Kind))
            return BusinessObjectRecordFailures.MissingUser<BusinessObjectRecordSubmitResultDto>();

        BusinessObjectRecord? record = await recordRepository.GetByIdForWorkspaceAsync(
            BusinessObjectRecordId.From(command.RecordId),
            workspaceId,
            cancellationToken);
        if (record is null)
            return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordSubmitResultDto>();

        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.RecordSubmit,
            BusinessObjectProductActions.RecordResourceType,
            record.ObjectKey.Value,
            command.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed)
            return decision.IsUnavailable
                ? BusinessObjectRecordFailures.AuthorizationUnavailable<BusinessObjectRecordSubmitResultDto>()
                : BusinessObjectRecordFailures.Forbidden<BusinessObjectRecordSubmitResultDto>();
        if (decision.Scope == ProductActionScope.Own
            && record.Owner != SubjectReferenceMapper.ToDomain(currentSubject.Subject))
            return BusinessObjectRecordFailures.NotFound<BusinessObjectRecordSubmitResultDto>();

        BusinessObjectDefinitionVersion? definition = await definitionRepository
            .GetPublishedVersionByIdForWorkspaceAsync(record.DefinitionVersionId, workspaceId, cancellationToken);
        if (definition is null)
            return BusinessObjectRecordFailures.DefinitionNotFound<BusinessObjectRecordSubmitResultDto>();

        if (record.Status == BusinessObjectRecordStatus.Submitted)
        {
            BusinessObjectRecordDetailDto submitted = BusinessObjectRecordMapper.ToDetailDto(record, definition);
            return new BusinessObjectRecordSubmitResultDto(true, submitted, submitted.RuleEvaluations);
        }

        if (command.ExpectedRevision != record.Revision)
            return BusinessObjectRecordFailures.Conflict<BusinessObjectRecordSubmitResultDto>(
                "The record has changed.");

        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> validValues =
            BusinessObjectRecordValueValidator.ValidateAndCanonicalize(definition, record.Values);
        if (validValues.IsFailure)
            return validValues.ErrorCode == ErrorCodes.FieldValidation && validValues.FieldErrors is not null
                ? BusinessObjectRecordFailures.Validation<BusinessObjectRecordSubmitResultDto>(validValues.FieldErrors)
                : BusinessObjectRecordFailures.Invalid<BusinessObjectRecordSubmitResultDto>(validValues.Error);

        Result<IReadOnlyList<BusinessObjectRecordRuleEvaluation>> evaluations = await ruleEvaluator.EvaluateAsync(
            workspaceId,
            definition,
            validValues.Value,
            cancellationToken);
        if (evaluations.IsFailure)
            return BusinessObjectRecordFailures.RuleExecutionFailed<BusinessObjectRecordSubmitResultDto>(
                evaluations.Error);

        IReadOnlyList<BusinessObjectRecordRuleEvaluationDto> evaluationDtos = evaluations.Value
            .Select(BusinessObjectRecordMapper.ToEvaluationDto)
            .ToArray();
        if (evaluations.Value.Any(evaluation => !evaluation.IsMatch))
        {
            BusinessObjectRecordDetailDto draft = BusinessObjectRecordMapper.ToDetailDto(record, definition);
            return new BusinessObjectRecordSubmitResultDto(false, draft, evaluationDtos);
        }

        Result submittedResult = record.Submit(
            command.ExpectedRevision,
            validValues.Value,
            evaluations.Value,
            SubjectReferenceMapper.ToDomain(currentSubject.Subject),
            DateTime.UtcNow);
        if (submittedResult.IsFailure)
            return submittedResult.ErrorCode == ErrorCodes.Conflict
                ? BusinessObjectRecordFailures.Conflict<BusinessObjectRecordSubmitResultDto>(submittedResult.Error)
                : BusinessObjectRecordFailures.Invalid<BusinessObjectRecordSubmitResultDto>(submittedResult.Error);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return BusinessObjectRecordFailures.Conflict<BusinessObjectRecordSubmitResultDto>(
                "The record has changed.");
        }

        BusinessObjectRecordDetailDto result = BusinessObjectRecordMapper.ToDetailDto(record, definition);
        return new BusinessObjectRecordSubmitResultDto(true, result, evaluationDtos);
    }
}
