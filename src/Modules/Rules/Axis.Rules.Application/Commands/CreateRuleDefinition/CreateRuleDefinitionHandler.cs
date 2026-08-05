using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.CreateRuleDefinition;

public sealed class CreateRuleDefinitionHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateRuleDefinitionCommand, RuleDefinitionDetailDto>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(
        CreateRuleDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();
        if (currentUser.UserId is not Guid userId)
            return RuleDefinitionFailures.MissingUser<RuleDefinitionDetailDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.CreateWorkspaceFromName(command.Name);
        if (key.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(key.Error);

        if (BuiltInRuleCatalog.Definitions.Any(definition => definition.Key == key.Value) ||
            await repository.KeyExistsAsync(key.Value, workspaceId, cancellationToken))
        {
            return RuleDefinitionFailures.DuplicateKey<RuleDefinitionDetailDto>();
        }

        Result<RuleDefinition> definition = RuleDefinition.CreateDraft(
            workspaceId,
            key.Value,
            command.Name,
            command.Description,
            userId,
            DateTime.UtcNow);
        if (definition.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(definition.Error);

        await repository.AddAsync(definition.Value, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return RuleDefinitionFailures.DuplicateKey<RuleDefinitionDetailDto>();
        }

        return RuleContractMapper.ToDetailDto(definition.Value);
    }
}
