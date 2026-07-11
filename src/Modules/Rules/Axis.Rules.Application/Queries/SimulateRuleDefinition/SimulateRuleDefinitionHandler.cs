using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using DomainLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;
using DomainOutcomeKind = Axis.Rules.Domain.RuleOutcomeKind;
using DomainScope = Axis.Rules.Domain.RuleScope;

namespace Axis.Rules.Application.Queries.SimulateRuleDefinition;

public sealed class SimulateRuleDefinitionHandler(
    ICurrentUser currentUser,
    RuleContextSchemaRegistry contextSchemas,
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

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        if (definition is null)
            return RuleDefinitionFailures.NotFound<RuleSimulationResultDto>();

        RuleEvaluationSource? source = query.DefinitionVersion is int version
            ? FromVersion(definition.FindVersion(version))
            : FromDraft(definition);
        if (source is null)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>("Requested rule draft or version is unavailable.");

        RuleContextSchema? schema = await contextSchemas.FindAsync(
            workspaceId,
            source.ContextKey,
            source.ContextSchemaVersion,
            cancellationToken);
        if (schema is null || schema.Scope != source.Scope)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>("Rule context schema is unavailable or incompatible.");

        Result<IReadOnlyDictionary<string, RuleValue>> parameters =
            RuleParameterValidator.Validate(source.Parameters, query.Parameters);
        if (parameters.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(parameters.Error);

        Result<IReadOnlyDictionary<string, RuleValue>> context = MapContext(schema, query.Context);
        if (context.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(context.Error);

        Result valid = RuleDefinitionValidator.Validate(
            schema,
            source.Parameters,
            source.Condition,
            source.Outcome,
            source.OutcomeKind);
        if (valid.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(valid.Error);

        cancellationToken.ThrowIfCancellationRequested();
        Result<RuleConditionEvaluation> evaluation = RuleConditionEvaluator.Evaluate(
            source.Condition,
            schema,
            context.Value,
            parameters.Value);
        if (evaluation.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleSimulationResultDto>(evaluation.Error);

        return new RuleSimulationResultDto(
            definition.Key.Value,
            query.DefinitionVersion,
            evaluation.Value.IsMatch,
            evaluation.Value.IsMatch ? RuleContractMapper.ToDto(source.Outcome) : null,
            evaluation.Value.Diagnostics
                .Select(diagnostic => new RuleNodeDiagnosticDto(diagnostic.NodeId, diagnostic.IsMatch))
                .ToArray(),
            NormalizeCorrelationId(query.CorrelationId));
    }

    private static Result<IReadOnlyDictionary<string, RuleValue>> MapContext(
        RuleContextSchema schema,
        IReadOnlyDictionary<string, RuleValueDto> context)
    {
        Dictionary<string, RuleValue> mapped = new(StringComparer.Ordinal);
        foreach ((string path, RuleValueDto valueDto) in context)
        {
            RuleContextField? field = schema.FindField(path);
            if (field is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>($"Rule context path '{path}' is not defined.");

            Result<RuleValue> value = RuleContractMapper.ToDomain(valueDto, field.AllowMultiple);
            if (value.IsFailure || value.Value.Type != field.Type)
            {
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>(
                    value.IsFailure ? value.Error : $"Rule context value '{path}' does not match its schema.");
            }

            mapped[path] = value.Value;
        }

        return mapped;
    }

    private static RuleEvaluationSource? FromDraft(RuleDefinition definition) =>
        definition.Status == DomainLifecycleStatus.Draft &&
        definition.Condition is not null &&
        definition.Outcome is not null
            ? new RuleEvaluationSource(
                definition.Scope,
                definition.ContextKey.Value,
                definition.ContextSchemaVersion,
                definition.OutcomeKind,
                definition.Parameters,
                definition.Condition,
                definition.Outcome)
            : null;

    private static RuleEvaluationSource? FromVersion(RuleDefinitionVersion? version) =>
        version is null
            ? null
            : new RuleEvaluationSource(
                version.Scope,
                version.ContextKey.Value,
                version.ContextSchemaVersion,
                version.OutcomeKind,
                version.Parameters,
                version.Condition,
                version.Outcome);

    private static string NormalizeCorrelationId(string correlationId) =>
        string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim()[..Math.Min(correlationId.Trim().Length, 120)];

    private sealed record RuleEvaluationSource(
        DomainScope Scope,
        string ContextKey,
        int ContextSchemaVersion,
        DomainOutcomeKind OutcomeKind,
        IReadOnlyList<RuleParameterDefinition> Parameters,
        RuleConditionNode Condition,
        RuleOutcome Outcome);
}
