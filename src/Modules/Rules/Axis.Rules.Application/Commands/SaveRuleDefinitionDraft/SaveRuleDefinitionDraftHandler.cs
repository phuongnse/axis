using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using DomainOutcomeKind = Axis.Rules.Domain.RuleOutcomeKind;
using DomainScope = Axis.Rules.Domain.RuleScope;

namespace Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;

public sealed class SaveRuleDefinitionDraftHandler(
    ICurrentUser currentUser,
    RuleContextSchemaRegistry contextSchemas,
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
        if (currentUser.UserId is not Guid userId)
            return RuleDefinitionFailures.MissingUser<RuleDefinitionDetailDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.DefinitionKey);
        if (key.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        if (definition is null)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        RuleContextSchema? contextSchema = await contextSchemas.FindAsync(
            workspaceId,
            command.ContextKey,
            command.ContextSchemaVersion,
            cancellationToken);
        if (contextSchema is null || contextSchema.Scope != (DomainScope)command.Scope)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>("Rule context schema is unavailable or incompatible.");

        Result<RuleDraftInput> input = RuleDraftInputMapper.Map(
            command.Parameters,
            command.Condition,
            command.Outcome);
        if (input.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(input.Error);

        Result validDefinition = RuleDefinitionValidator.Validate(
            contextSchema,
            input.Value.Parameters,
            input.Value.Condition,
            input.Value.Outcome,
            (DomainOutcomeKind)command.OutcomeKind);
        if (validDefinition.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(validDefinition.Error);

        Result saved = definition.SaveDraft(
            command.ExpectedRevision,
            command.Name,
            command.Description,
            (DomainScope)command.Scope,
            contextSchema.Key,
            contextSchema.Version,
            (DomainOutcomeKind)command.OutcomeKind,
            input.Value.Parameters,
            input.Value.Condition,
            input.Value.Outcome,
            userId,
            DateTime.UtcNow);
        if (saved.IsFailure)
            return saved.ErrorCode == ErrorCodes.Conflict
                ? RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>(saved.Error)
                : RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(saved.Error);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>("The rule definition has changed.");
        }
        return RuleContractMapper.ToDetailDto(definition);
    }
}
