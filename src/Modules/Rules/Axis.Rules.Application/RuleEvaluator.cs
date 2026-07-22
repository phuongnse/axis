using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractOutcomeKind = Axis.Rules.Contracts.RuleOutcomeKind;
using ContractRuleDecision = Axis.Rules.Contracts.RuleDecision;
using ContractRuleScope = Axis.Rules.Contracts.RuleScope;
using ContractRuleSeverity = Axis.Rules.Contracts.RuleSeverity;
using DomainOutcomeKind = Axis.Rules.Domain.RuleOutcomeKind;

namespace Axis.Rules.Application;

public sealed class RuleEvaluator(
    RuleContextSchemaRegistry contextSchemas,
    IRuleDefinitionRepository repository) : IRuleEvaluator
{
    public async Task<RuleEvaluationResult> EvaluateAsync(
        RuleEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        string correlationId = NormalizeCorrelationId(request.CorrelationId);
        if (request.WorkspaceId == Guid.Empty)
            return Failed(request, correlationId, "workspace_required", "Workspace scope is required.");
        if (!Enum.IsDefined(request.Purpose) || !Enum.IsDefined(request.Scope))
            return Failed(request, correlationId, "request_invalid", "Rule evaluation purpose or scope is invalid.");

        try
        {
            RuleContextSchema? schema = await contextSchemas.FindAsync(
                request.WorkspaceId,
                request.ContextKey,
                request.ContextSchemaVersion,
                cancellationToken);
            if (schema is null || (ContractRuleScope)schema.Scope != request.Scope)
            {
                return Failed(
                    request,
                    correlationId,
                    "context_schema_incompatible",
                    "Rule context schema is unavailable or incompatible.");
            }

            Result<IReadOnlyDictionary<string, RuleValue>> context = MapContext(schema, request.Context);
            if (context.IsFailure)
                return Failed(request, correlationId, "context_invalid", context.Error);

            List<RuleEvaluationItemDto> items = [];
            List<RuleViolationDto> violations = [];
            bool hasMatchedDeny = false;
            foreach (RuleEvaluationReference reference in request.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Result<ResolvedRule> resolved = await ResolveAsync(
                    request,
                    schema,
                    reference,
                    cancellationToken);
                if (resolved.IsFailure)
                    return Failed(request, correlationId, resolved.ErrorCode!, resolved.Error);

                Result<bool> match = EvaluateResolved(resolved.Value, schema, context.Value);
                if (match.IsFailure)
                    return Failed(request, correlationId, "evaluation_failed", match.Error);

                RuleOutcomeDto? outcome = match.Value ? resolved.Value.Outcome : null;
                items.Add(new RuleEvaluationItemDto(
                    reference.DefinitionKey,
                    reference.DefinitionVersion,
                    match.Value,
                    outcome));

                if (!match.Value || outcome is null)
                    continue;

                if (outcome.Kind == ContractOutcomeKind.Validation)
                {
                    violations.Add(new RuleViolationDto(
                        reference.DefinitionKey,
                        reference.DefinitionVersion,
                        outcome.ViolationCode!,
                        outcome.Severity!.Value,
                        outcome.Message!));
                }
                else if (outcome.Decision == ContractRuleDecision.Deny)
                {
                    hasMatchedDeny = true;
                }
            }

            bool isAllowed = request.Purpose == ContractOutcomeKind.Validation
                ? violations.All(violation => violation.Severity != ContractRuleSeverity.Error)
                : !hasMatchedDeny;
            return new RuleEvaluationResult(
                true,
                isAllowed,
                request.ContextSchemaVersion,
                violations,
                items,
                correlationId,
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(request, correlationId, "evaluation_failed", "Rule evaluation failed.");
        }
    }

    private async Task<Result<ResolvedRule>> ResolveAsync(
        RuleEvaluationRequest request,
        RuleContextSchema schema,
        RuleEvaluationReference reference,
        CancellationToken cancellationToken)
    {
        SystemRuleDefinition? system = SystemRuleCatalog.Find(
            reference.DefinitionKey,
            reference.DefinitionVersion);
        if (system is not null)
        {
            if ((ContractRuleScope)system.Scope != request.Scope ||
                (ContractOutcomeKind)system.OutcomeKind != request.Purpose)
            {
                return Result.Failure<ResolvedRule>(
                    "rule_incompatible",
                    "Rule version is incompatible with the evaluation request.");
            }

            if (schema.TargetTypeKey is null)
            {
                return Result.Failure<ResolvedRule>(
                    "rule_incompatible",
                    "Rule context schema does not declare system-rule applicability metadata.");
            }

            RuleApplicationValidationResult validation = RuleApplicationValidator.ValidateSystem(
                system,
                new RuleApplicationTarget(
                    request.Scope,
                    request.ContextKey,
                    request.ContextSchemaVersion,
                    schema.TargetTypeKey,
                    schema.Configuration),
                reference.Parameters);
            if (!validation.IsValid)
            {
                return Result.Failure<ResolvedRule>(
                    validation.ErrorCode ?? "parameter_invalid",
                    validation.Error ?? "Rule application is invalid.");
            }

            Result<IReadOnlyDictionary<string, RuleValue>> parameters = MapParameters(
                system.Parameters,
                validation.CanonicalParameters!);
            if (parameters.IsFailure)
                return Result.Failure<ResolvedRule>("parameter_invalid", parameters.Error);

            return new ResolvedRule(
                system.Condition,
                RuleContractMapper.ToDto(system.Outcome),
                parameters.Value);
        }

        if (SystemRuleCatalog.Definitions.Any(definition =>
                definition.Key.Value.Equals(reference.DefinitionKey, StringComparison.Ordinal)))
        {
            return Result.Failure<ResolvedRule>("version_not_found", "Published rule version was not found.");
        }

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(reference.DefinitionKey);
        if (key.IsFailure)
            return Result.Failure<ResolvedRule>("definition_not_found", "Rule definition was not found.");

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            request.WorkspaceId,
            cancellationToken);
        if (definition is null)
            return Result.Failure<ResolvedRule>("definition_not_found", "Rule definition was not found.");

        RuleDefinitionVersion? version = definition.FindVersion(reference.DefinitionVersion);
        if (version is null)
            return Result.Failure<ResolvedRule>("version_not_found", "Published rule version was not found.");

        if ((ContractRuleScope)version.Scope != request.Scope ||
            (ContractOutcomeKind)version.OutcomeKind != request.Purpose ||
            version.ContextKey != schema.Key ||
            version.ContextSchemaVersion != schema.Version)
        {
            return Result.Failure<ResolvedRule>(
            "rule_incompatible",
            "Rule version is incompatible with the evaluation request.");
        }

        Result<IReadOnlyDictionary<string, RuleValue>> mappedParameters =
            MapParameters(version.Parameters, reference.Parameters);
        if (mappedParameters.IsFailure)
            return Result.Failure<ResolvedRule>("parameter_invalid", mappedParameters.Error);

        Result valid = RuleDefinitionValidator.Validate(
            schema,
            version.Parameters,
            version.Condition,
            version.Outcome,
            version.OutcomeKind);
        if (valid.IsFailure)
            return Result.Failure<ResolvedRule>("definition_invalid", valid.Error);

        return new ResolvedRule(
            version.Condition,
            RuleContractMapper.ToDto(version.Outcome),
            mappedParameters.Value);
    }

    private static Result<bool> EvaluateResolved(
        ResolvedRule rule,
        RuleContextSchema schema,
        IReadOnlyDictionary<string, RuleValue> context)
    {
        Result<RuleConditionEvaluation> evaluation = RuleConditionEvaluator.Evaluate(
            rule.Condition,
            schema,
            context,
            rule.Parameters);
        return evaluation.IsSuccess
            ? evaluation.Value.IsMatch
            : Result.Failure<bool>(evaluation.Error);
    }

    private static Result<IReadOnlyDictionary<string, RuleValue>> MapParameters(
        IReadOnlyList<RuleParameterDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        Dictionary<string, RuleValueDto> values = new(StringComparer.Ordinal);
        foreach ((string key, IReadOnlyList<string> rawValues) in parameters)
        {
            if (key is null || rawValues is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameters are invalid.");

            string normalizedKey = key.Trim();
            RuleParameterDefinition? definition = definitions.SingleOrDefault(candidate =>
                candidate.Key.Equals(normalizedKey, StringComparison.Ordinal));
            if (definition is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameter is not supported.");

            if (values.ContainsKey(normalizedKey))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameter keys must be unique.");

            values[normalizedKey] = new RuleValueDto(
                (Contracts.RuleValueType)definition.Type,
                rawValues);
        }

        return RuleParameterValidator.Validate(definitions, values);
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

    private static RuleEvaluationResult Failed(
        RuleEvaluationRequest request,
        string correlationId,
        string errorCode,
        string error) =>
        new(
            false,
            false,
            request.ContextSchemaVersion,
            [],
            [],
            correlationId,
            errorCode,
            error);

    private static string NormalizeCorrelationId(string correlationId)
    {
        string normalized = correlationId?.Trim() ?? string.Empty;
        return normalized.Length == 0
            ? Guid.NewGuid().ToString("N")
            : normalized[..Math.Min(normalized.Length, 120)];
    }

    private sealed record ResolvedRule(
        RuleConditionNode Condition,
        RuleOutcomeDto Outcome,
        IReadOnlyDictionary<string, RuleValue> Parameters);
}
