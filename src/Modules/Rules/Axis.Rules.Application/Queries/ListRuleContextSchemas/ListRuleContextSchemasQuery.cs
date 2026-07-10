using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ListRuleContextSchemas;

public sealed record ListRuleContextSchemasQuery
    : IQuery<Result<IReadOnlyList<RuleContextSchemaDto>>>;
