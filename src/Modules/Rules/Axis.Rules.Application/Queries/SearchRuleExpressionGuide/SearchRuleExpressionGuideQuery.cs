using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.SearchRuleExpressionGuide;

public sealed record SearchRuleExpressionGuideQuery(
    SearchRuleExpressionGuideRequest Request)
    : IQuery<Result<RuleExpressionGuideDto>>;
