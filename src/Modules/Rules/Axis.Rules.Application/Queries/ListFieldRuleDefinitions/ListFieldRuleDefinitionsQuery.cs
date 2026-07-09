using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Queries.ListFieldRuleDefinitions;

public sealed record ListFieldRuleDefinitionsQuery : IQuery<IReadOnlyList<FieldRuleDefinitionDto>>;
