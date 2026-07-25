using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.SearchRuleExpressionGuide;

public sealed class SearchRuleExpressionGuideHandler(
    ICurrentUser currentUser,
    RuleExpressionGuideService guide)
    : IQueryHandler<SearchRuleExpressionGuideQuery, Result<RuleExpressionGuideDto>>
{
    public Task<Result<RuleExpressionGuideDto>> Handle(
        SearchRuleExpressionGuideQuery query,
        CancellationToken cancellationToken) =>
        currentUser.workspaceId is not Guid workspaceId
            ? Task.FromResult(RuleDefinitionFailures.MissingWorkspace<RuleExpressionGuideDto>())
            : guide.SearchAsync(workspaceId, query.Request, cancellationToken);
}
