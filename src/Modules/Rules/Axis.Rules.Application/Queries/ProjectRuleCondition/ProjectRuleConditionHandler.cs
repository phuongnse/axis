using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ProjectRuleCondition;

public sealed class ProjectRuleConditionHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    RuleConditionProjectionService projection)
    : IQueryHandler<ProjectRuleConditionQuery, Result<RuleConditionProjectionDto>>
{
    public async Task<Result<RuleConditionProjectionDto>> Handle(
        ProjectRuleConditionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleConditionProjectionDto>();
        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject,
            RuleProductActions.DefinitionManage, RuleProductActions.DefinitionResourceType,
            null, null, cancellationToken);
        return decision.IsAllowed
            ? projection.Project(query.Request)
            : RuleDefinitionFailures.Authorization<RuleConditionProjectionDto>(decision);
    }
}
