using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleBinding;

public sealed record GetRuleBindingQuery(Guid BindingId)
    : IQuery<Result<RuleBindingDto>>;
