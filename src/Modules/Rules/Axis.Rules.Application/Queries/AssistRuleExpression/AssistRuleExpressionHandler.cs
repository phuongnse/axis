using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.AssistRuleExpression;

public sealed class AssistRuleExpressionHandler(
    ICurrentUser currentUser,
    RuleExpressionAuthoringService authoring)
    : IQueryHandler<AssistRuleExpressionQuery, Result<RuleExpressionAuthoringDto>>
{
    public Task<Result<RuleExpressionAuthoringDto>> Handle(
        AssistRuleExpressionQuery query,
        CancellationToken cancellationToken) =>
        currentUser.workspaceId is not Guid workspaceId
            ? Task.FromResult(RuleDefinitionFailures.MissingWorkspace<RuleExpressionAuthoringDto>())
            : authoring.AssistAsync(workspaceId, query.Request, cancellationToken);
}
