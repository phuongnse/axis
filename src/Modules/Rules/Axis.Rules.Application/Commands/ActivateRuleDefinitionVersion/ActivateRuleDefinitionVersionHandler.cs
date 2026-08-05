using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.ActivateRuleDefinitionVersion;

public sealed class ActivateRuleDefinitionVersionHandler(ICurrentUser currentUser, IRuleDefinitionRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<ActivateRuleDefinitionVersionCommand, RuleDefinitionDetailDto>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(ActivateRuleDefinitionVersionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId) return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();
        if (currentUser.UserId is not Guid userId) return RuleDefinitionFailures.MissingUser<RuleDefinitionDetailDto>();
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.DefinitionKey);
        if (key.IsFailure) return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();
        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(key.Value, workspaceId, cancellationToken);
        if (definition is null) return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();
        Result activated = definition.ActivateVersion(command.ExpectedRevision, command.Version, userId, DateTime.UtcNow);
        if (activated.IsFailure) return activated.ErrorCode == ErrorCodes.Conflict ? RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>(activated.Error) : RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(activated.Error);
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (ConcurrencyException) { return RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>("The rule definition has changed."); }
        return RuleContractMapper.ToDetailDto(definition);
    }
}
