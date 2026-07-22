using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleExpressionLanguage;

public sealed class GetRuleExpressionLanguageHandler
    : IQueryHandler<GetRuleExpressionLanguageQuery, Result<RuleExpressionLanguageDto>>
{
    public Task<Result<RuleExpressionLanguageDto>> Handle(
        GetRuleExpressionLanguageQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(RuleContractMapper.ToExpressionLanguageDto()));
}
