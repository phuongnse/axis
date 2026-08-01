using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.SimulateRuleDefinition;

public sealed class SimulateRuleDefinitionHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository repository)
    : IQueryHandler<SimulateRuleDefinitionQuery, Result<RuleSimulationResultDto>>
{
    public async Task<Result<RuleSimulationResultDto>> Handle(
        SimulateRuleDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleSimulationResultDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(query.DefinitionKey);
        if (key.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleSimulationResultDto>();

        RuleDefinition? definition = SystemRuleCatalog.Definitions
            .FirstOrDefault(candidate => candidate.Key == key.Value);
        definition ??= await repository.GetByKeyForWorkspaceAsync(key.Value, workspaceId, cancellationToken);
        if (definition is null)
            return RuleDefinitionFailures.NotFound<RuleSimulationResultDto>();

        RuleConditionNode? condition = query.DefinitionVersion is int version
            ? definition.FindVersion(version)?.Condition
            : definition.Condition;
        IReadOnlyList<RuleInputDefinition>? inputs = query.DefinitionVersion is int selectedVersion
            ? definition.FindVersion(selectedVersion)?.Inputs
            : definition.Inputs;
        if (condition is null || inputs is null)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>("Requested rule draft or version is unavailable.");

        Result<IReadOnlyDictionary<string, RuleValue>> mappedInputs =
            RuleInputValidator.Validate(inputs, query.Inputs);
        if (mappedInputs.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(mappedInputs.Error);

        RuleOutputContract? output = query.DefinitionVersion is int outputVersion
            ? definition.FindVersion(outputVersion)?.Output
            : definition.Output;
        if (output is null)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>("Requested rule output contract is unavailable.");

        Result valid = RuleDefinitionValidator.Validate(inputs, condition, output);
        if (valid.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(valid.Error);

        Result<RuleConditionEvaluation> evaluation = RuleConditionEvaluator.Evaluate(
            condition,
            mappedInputs.Value);
        if (evaluation.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(evaluation.Error);

        return new RuleSimulationResultDto(
            definition.Key.Value,
            query.DefinitionVersion,
            evaluation.Value.IsMatch,
            evaluation.Value.Diagnostics
                .Select(diagnostic => new RuleNodeDiagnosticDto(diagnostic.NodeId, diagnostic.IsMatch))
                .ToArray(),
            NormalizeCorrelationId(query.CorrelationId));
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        string normalized = correlationId?.Trim() ?? string.Empty;
        return normalized.Length == 0
            ? Guid.NewGuid().ToString("N")
            : normalized[..Math.Min(normalized.Length, 120)];
    }
}
