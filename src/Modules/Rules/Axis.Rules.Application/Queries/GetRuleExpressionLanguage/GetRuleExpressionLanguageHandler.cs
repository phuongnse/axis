using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleExpressionLanguage;

public sealed class GetRuleExpressionLanguageHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization)
    : IQueryHandler<GetRuleExpressionLanguageQuery, Result<RuleExpressionLanguageDto>>
{
    public async Task<Result<RuleExpressionLanguageDto>> Handle(
        GetRuleExpressionLanguageQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleExpressionLanguageDto>();
        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject,
            RuleProductActions.DefinitionRead, RuleProductActions.DefinitionResourceType,
            null, null, cancellationToken);
        return decision.IsAllowed
            ? Result.Success(RuleContractMapper.ToExpressionLanguageDto())
            : RuleDefinitionFailures.Authorization<RuleExpressionLanguageDto>(decision);
    }
}
