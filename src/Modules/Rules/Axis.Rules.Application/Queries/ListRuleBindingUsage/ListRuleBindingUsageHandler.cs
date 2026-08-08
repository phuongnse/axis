using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ListRuleBindingUsage;

public sealed class ListRuleBindingUsageHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IRuleBindingRepository repository)
    : IQueryHandler<ListRuleBindingUsageQuery, Result<IReadOnlyList<RuleBindingUsageDto>>>
{
    public async Task<Result<IReadOnlyList<RuleBindingUsageDto>>> Handle(
        ListRuleBindingUsageQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return Result.Failure<IReadOnlyList<RuleBindingUsageDto>>(ErrorCodes.Forbidden, "Current workspace scope is required.");
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(query.DefinitionKey);
        if (key.IsFailure || query.Version <= 0)
            return Result.Failure<IReadOnlyList<RuleBindingUsageDto>>(ErrorCodes.InvalidInput, "Rule version reference is invalid.");
        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(
                authorization, workspaceId, currentSubject.Subject,
                RuleProductActions.BindingRead, RuleProductActions.BindingResourceType,
                key.Value.Value, null, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<IReadOnlyList<RuleBindingUsageDto>>(decision);
        IReadOnlyList<RuleBinding> bindings = await repository.ListByDefinitionAsync(
            key.Value, query.Version, workspaceId, cancellationToken);
        return bindings.Select(RuleBindingContractMapper.ToUsageDto).ToArray();
    }
}
