using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.SearchRuleExpressionGuide;

public sealed class SearchRuleExpressionGuideHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    RuleExpressionGuideService guide)
    : IQueryHandler<SearchRuleExpressionGuideQuery, Result<RuleExpressionGuideDto>>
{
    public Task<Result<RuleExpressionGuideDto>> Handle(
        SearchRuleExpressionGuideQuery query,
        CancellationToken cancellationToken) =>
        AuthorizeAndSearchAsync(query, cancellationToken);

    private async Task<Result<RuleExpressionGuideDto>> AuthorizeAndSearchAsync(
        SearchRuleExpressionGuideQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleExpressionGuideDto>();
        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(
                authorization, workspaceId, currentSubject.Subject,
                RuleProductActions.DefinitionRead, RuleProductActions.DefinitionResourceType,
                null, null, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<RuleExpressionGuideDto>(decision);
        return await guide.SearchAsync(workspaceId, query.Request, cancellationToken);
    }
}
