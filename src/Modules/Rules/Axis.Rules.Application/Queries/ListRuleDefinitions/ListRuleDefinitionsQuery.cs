using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ListRuleDefinitions;

public sealed record ListRuleDefinitionsQuery(
    int Page,
    int PageSize,
    RuleScope? Scope = null,
    RuleOrigin? Origin = null,
    RuleLifecycleStatus? Status = null)
    : IQuery<Result<PagedResult<RuleDefinitionSummaryDto>>>;
