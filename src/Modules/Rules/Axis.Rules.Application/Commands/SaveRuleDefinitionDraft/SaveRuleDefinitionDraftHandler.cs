using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;

public sealed class SaveRuleDefinitionDraftHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization authorization,
    IRuleDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SaveRuleDefinitionDraftCommand, RuleDefinitionDetailDto>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(
        SaveRuleDefinitionDraftCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.DefinitionKey);
        if (key.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        WorkspaceProductBuilderDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<RuleDefinitionDetailDto>(decision);

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        if (definition is null)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        Result<RuleDraftInput> input = RuleDraftInputMapper.Map(
            command.Inputs,
            command.Condition);
        if (input.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(input.Error);

        Result validDefinition = RuleDefinitionValidator.Validate(input.Value.Inputs, input.Value.Condition, definition.Output);
        if (validDefinition.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(validDefinition.Error);

        Result saved = definition.SaveDraft(
            command.ExpectedRevision,
            command.Name,
            command.Description,
            input.Value.Inputs,
            input.Value.Condition,
            RuleSubjectReferenceMapper.ToDomain(currentSubject.Subject),
            DateTime.UtcNow);
        if (saved.IsFailure)
            return saved.ErrorCode == ErrorCodes.Conflict
                ? RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>(saved.Error)
                : RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(saved.Error);
        Result provenance = definition.RecordModification(RuleActor.From(currentSubject));
        if (provenance.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(provenance.Error);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>("The rule definition has changed.");
        }
        return RuleContractMapper.ToDetailDto(definition, canManage: true);
    }
}
