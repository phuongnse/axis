using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;

public sealed class PublishBusinessObjectDefinitionHandler(
    ICurrentUser currentUser,
    IBusinessObjectDefinitionRepository repository,
    IUnitOfWork unitOfWork,
    IRuleBindingReferenceValidator bindingReferenceValidator)
    : ICommandHandler<PublishBusinessObjectDefinitionCommand, BusinessObjectDefinitionDetailDto>
{
    public async Task<Result<BusinessObjectDefinitionDetailDto>> Handle(
        PublishBusinessObjectDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return BusinessObjectDefinitionFailures.MissingWorkspace<BusinessObjectDefinitionDetailDto>();

        if (currentUser.UserId is not Guid userId)
            return BusinessObjectDefinitionFailures.MissingUser<BusinessObjectDefinitionDetailDto>();

        BusinessObjectDefinition? definition = await repository.GetByIdForWorkspaceAsync(
            BusinessObjectDefinitionId.From(command.BusinessObjectDefinitionId),
            workspaceId,
            cancellationToken);
        if (definition is null)
            return BusinessObjectDefinitionFailures.NotFound<BusinessObjectDefinitionDetailDto>();

        foreach (BusinessObjectFieldDefinition field in definition.Fields)
        {
            foreach (BusinessObjectFieldRule rule in field.Rules)
            {
                RuleBindingReferenceValidationResult validation = await bindingReferenceValidator.ValidateAsync(
                    new RuleBindingReferenceValidationRequest(
                        workspaceId,
                        rule.BindingId,
                        BusinessObjectRecordRuleBindingContract.TargetType,
                        BusinessObjectRecordRuleBindingContract.TargetId(definition.Key, field.Key.Value),
                        BusinessObjectRecordRuleBindingContract.UseCaseOrTrigger,
                        BusinessObjectRuleBindingContextSchema.For(
                            field.FieldType,
                            field.ChoiceSelectionMode),
                        BusinessObjectRuleBindingContextSchema.RequiredKeys,
                        rule.BindingRevision),
                    cancellationToken);
                if (!validation.IsValid)
                    return validation.ErrorCode == "binding_revision_conflict"
                        ? BusinessObjectDefinitionFailures.Conflict<BusinessObjectDefinitionDetailDto>(validation.Error!)
                        : BusinessObjectDefinitionFailures.Invalid<BusinessObjectDefinitionDetailDto>(validation.Error!);
            }
        }

        Result<BusinessObjectDefinitionVersion> published = definition.Publish(
            command.ExpectedRevision,
            userId,
            DateTime.UtcNow);
        if (published.IsFailure)
            return MapDomainFailure(published);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return BusinessObjectDefinitionFailures.Conflict<BusinessObjectDefinitionDetailDto>(
                "The object definition has changed.");
        }

        return BusinessObjectDefinitionMapper.ToDetailDto(definition);
    }

    private static Result<BusinessObjectDefinitionDetailDto> MapDomainFailure(Result result) =>
        result.ErrorCode == ErrorCodes.Conflict
            ? BusinessObjectDefinitionFailures.Conflict<BusinessObjectDefinitionDetailDto>(result.Error)
            : BusinessObjectDefinitionFailures.Invalid<BusinessObjectDefinitionDetailDto>(result.Error);
}
