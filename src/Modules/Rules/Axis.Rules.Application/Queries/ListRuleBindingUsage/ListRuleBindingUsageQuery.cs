using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ListRuleBindingUsage;

public sealed record ListRuleBindingUsageQuery(string DefinitionKey, int Version)
    : IQuery<Result<IReadOnlyList<RuleBindingUsageDto>>>;
