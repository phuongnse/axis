using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.SimulateRuleDefinition;

public sealed record SimulateRuleDefinitionQuery(
    string DefinitionKey,
    int? DefinitionVersion,
    IReadOnlyDictionary<string, RuleValueDto> Parameters,
    IReadOnlyDictionary<string, RuleValueDto> Context,
    string CorrelationId)
    : IQuery<Result<RuleSimulationResultDto>>;
