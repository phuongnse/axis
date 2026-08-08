using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleDefinition;

public sealed class GetRuleDefinitionHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IRuleDefinitionRepository repository)
    : IQueryHandler<GetRuleDefinitionQuery, Result<RuleDefinitionDetailDto>>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(
        GetRuleDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(query.DefinitionKey);
        if (key.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        ProductAuthorizationDecision readDecision = await RuleAuthorization.AuthorizeAsync(
                authorization, workspaceId, currentSubject.Subject,
                RuleProductActions.DefinitionRead, RuleProductActions.DefinitionResourceType,
                key.Value.Value, null, cancellationToken);
        if (!readDecision.IsAllowed)
            return RuleDefinitionFailures.Authorization<RuleDefinitionDetailDto>(readDecision);

        ProductAuthorizationDecision manageDecision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject,
            RuleProductActions.DefinitionManage, RuleProductActions.DefinitionResourceType,
            key.Value.Value, null, cancellationToken);
        if (manageDecision.IsUnavailable)
            return RuleDefinitionFailures.Authorization<RuleDefinitionDetailDto>(manageDecision);

        RuleDefinition? builtIn = BuiltInRuleCatalog.Definitions
            .Where(definition => definition.Key == key.Value)
            .FirstOrDefault();
        if (builtIn is not null)
            return RuleContractMapper.ToDetailDto(builtIn, canManage: false);

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        return definition is null
            ? RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>()
            : RuleContractMapper.ToDetailDto(definition, manageDecision.IsAllowed);
    }
}
