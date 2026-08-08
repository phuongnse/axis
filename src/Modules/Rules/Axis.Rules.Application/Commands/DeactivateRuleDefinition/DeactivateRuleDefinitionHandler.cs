using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.DeactivateRuleDefinition;

public sealed class DeactivateRuleDefinitionHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IRuleDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivateRuleDefinitionCommand, RuleDefinitionDetailDto>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(DeactivateRuleDefinitionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId) return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.DefinitionKey);
        if (key.IsFailure) return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();
        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(authorization, workspaceId, currentSubject.Subject, RuleProductActions.DefinitionManage, RuleProductActions.DefinitionResourceType, key.Value.Value, null, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<RuleDefinitionDetailDto>(decision);
        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(key.Value, workspaceId, cancellationToken);
        if (definition is null) return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();
        Result deactivated = definition.Deactivate(command.ExpectedRevision, RuleSubjectReferenceMapper.ToDomain(currentSubject.Subject), DateTime.UtcNow);
        if (deactivated.IsFailure) return deactivated.ErrorCode == ErrorCodes.Conflict ? RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>(deactivated.Error) : RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(deactivated.Error);
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (ConcurrencyException) { return RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>("The rule definition has changed."); }
        return RuleContractMapper.ToDetailDto(definition, canManage: true);
    }
}
