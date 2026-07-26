using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.AssistRuleExpression;

public sealed record AssistRuleExpressionQuery(AssistRuleExpressionRequest Request)
    : IQuery<Result<RuleExpressionAuthoringDto>>;
