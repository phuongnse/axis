using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ProjectRuleCondition;

public sealed class ProjectRuleConditionHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization authorization,
    RuleConditionProjectionService projection)
    : IQueryHandler<ProjectRuleConditionQuery, Result<RuleConditionProjectionDto>>
{
    public async Task<Result<RuleConditionProjectionDto>> Handle(
        ProjectRuleConditionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleConditionProjectionDto>();
        WorkspaceProductBuilderDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject, cancellationToken);
        return decision.IsAllowed
            ? projection.Project(query.Request)
            : RuleDefinitionFailures.Authorization<RuleConditionProjectionDto>(decision);
    }
}
