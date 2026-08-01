using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ProjectRuleCondition;

public sealed class ProjectRuleConditionHandler(
    ICurrentUser currentUser,
    RuleConditionProjectionService projection)
    : IQueryHandler<ProjectRuleConditionQuery, Result<RuleConditionProjectionDto>>
{
    public Task<Result<RuleConditionProjectionDto>> Handle(
        ProjectRuleConditionQuery query,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return currentUser.workspaceId is null
            ? Task.FromResult(RuleDefinitionFailures.MissingWorkspace<RuleConditionProjectionDto>())
            : Task.FromResult(projection.Project(query.Request));
    }
}
