using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.PublishRuleDefinition;

public sealed class PublishRuleDefinitionHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<PublishRuleDefinitionCommand, RuleDefinitionDetailDto>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(
        PublishRuleDefinitionCommand command,
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

        if (definition.Condition is null)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>("Rule draft is incomplete.");

        Result valid = RuleDefinitionValidator.Validate(definition.Inputs, definition.Condition, definition.Output);
        if (valid.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(valid.Error);

        Result<RuleDefinitionVersion> published = definition.Publish(
            command.ExpectedRevision,
            userId,
            DateTime.UtcNow);
        if (published.IsFailure)
            return published.ErrorCode == ErrorCodes.Conflict
                ? RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>(published.Error)
                : RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(published.Error);

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
