using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleDefinition;

public sealed record GetRuleDefinitionQuery(string DefinitionKey)
    : IQuery<Result<RuleDefinitionDetailDto>>;
