using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ListRuleDefinitions;

public sealed record ListRuleDefinitionsQuery(
    int Page,
    int PageSize,
    RuleOrigin? Origin = null,
    RuleLifecycleStatus? Status = null,
    string? SearchQuery = null,
    string? Language = null)
    : IQuery<Result<PagedResult<RuleDefinitionSummaryDto>>>;
