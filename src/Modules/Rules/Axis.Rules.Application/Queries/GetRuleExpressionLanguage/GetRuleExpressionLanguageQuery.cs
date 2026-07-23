using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleExpressionLanguage;

public sealed record GetRuleExpressionLanguageQuery
    : IQuery<Result<RuleExpressionLanguageDto>>;
