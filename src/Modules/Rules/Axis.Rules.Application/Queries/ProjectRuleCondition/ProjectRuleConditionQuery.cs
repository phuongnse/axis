using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ProjectRuleCondition;

public sealed record ProjectRuleConditionQuery(ProjectRuleConditionRequest Request)
    : IQuery<Result<RuleConditionProjectionDto>>;
