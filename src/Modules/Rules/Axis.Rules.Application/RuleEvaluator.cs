using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

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

            return ResolvedRule.System(system, parameters.Value);
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

        return ResolvedRule.Workspace(
            version.Condition,
            RuleContractMapper.ToDto(version.Outcome),
            mappedParameters.Value);
    }

    private static Result<bool> EvaluateResolved(
        ResolvedRule rule,
        RuleContextSchema schema,
        IReadOnlyDictionary<string, RuleValue> context)
    {
        if (rule.SystemDefinition is not null)
            return EvaluateSystem(rule.SystemDefinition, context, rule.Parameters);

        Result<RuleConditionEvaluation> evaluation = RuleConditionEvaluator.Evaluate(
            rule.Condition!,
            schema,
            context,
            rule.Parameters);
        return evaluation.IsSuccess
            ? evaluation.Value.IsMatch
            : Result.Failure<bool>(evaluation.Error);
    }

    private static Result<bool> EvaluateSystem(
        SystemRuleDefinition definition,
        IReadOnlyDictionary<string, RuleValue> context,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        context.TryGetValue("field.value", out RuleValue? value);
        if (definition.Key.Value == RuleDefinitionKeys.Required)
        {
            return value is null || value.Values.Count == 0 ||
                value.Type == Domain.RuleValueType.Text && value.Values.All(string.IsNullOrWhiteSpace);
        }

        if (value is null)
            return false;

        return definition.Key.Value switch
        {
            RuleDefinitionKeys.NumericRange => OutsideDecimalRange(value, parameters),
            RuleDefinitionKeys.DecimalPrecision => ExceedsDecimalPrecision(value, parameters),
            RuleDefinitionKeys.DateRange => OutsideDateRange(value, parameters),
            RuleDefinitionKeys.DateTimeRange => OutsideDateTimeRange(value, parameters),
            RuleDefinitionKeys.TextLength => OutsideTextLength(value, parameters),
            RuleDefinitionKeys.TextPattern => DoesNotMatchPattern(value, parameters),
            RuleDefinitionKeys.TextFormat => DoesNotMatchFormat(value, parameters),
            RuleDefinitionKeys.ChoiceSelectionCount => OutsideSelectionCount(value, parameters),
            _ => Result.Failure<bool>("System rule definition is not supported."),
        };
    }

    private static bool OutsideDecimalRange(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        decimal number = decimal.Parse(value.Values[0], CultureInfo.InvariantCulture);
        return parameters.TryGetValue("min", out RuleValue? min) &&
                number < decimal.Parse(min.Values[0], CultureInfo.InvariantCulture) ||
            parameters.TryGetValue("max", out RuleValue? max) &&
                number > decimal.Parse(max.Values[0], CultureInfo.InvariantCulture);
    }

    private static bool ExceedsDecimalPrecision(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        string canonical = decimal.Parse(value.Values[0], CultureInfo.InvariantCulture)
            .ToString("G29", CultureInfo.InvariantCulture)
            .TrimStart('-');
        string[] parts = canonical.Split('.', 2);
        int scale = parts.Length == 2 ? parts[1].Length : 0;
        int precision = Math.Max(1, string.Concat(parts).TrimStart('0').Length);
        return precision > int.Parse(parameters["precision"].Values[0], CultureInfo.InvariantCulture) ||
            scale > int.Parse(parameters["scale"].Values[0], CultureInfo.InvariantCulture);
    }

    private static bool OutsideDateRange(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        DateOnly date = DateOnly.ParseExact(value.Values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return parameters.TryGetValue("min", out RuleValue? min) &&
                date < DateOnly.ParseExact(min.Values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture) ||
            parameters.TryGetValue("max", out RuleValue? max) &&
                date > DateOnly.ParseExact(max.Values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool OutsideDateTimeRange(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        DateTimeOffset instant = DateTimeOffset.Parse(value.Values[0], CultureInfo.InvariantCulture);
        return parameters.TryGetValue("min", out RuleValue? min) &&
                instant < DateTimeOffset.Parse(min.Values[0], CultureInfo.InvariantCulture) ||
            parameters.TryGetValue("max", out RuleValue? max) &&
                instant > DateTimeOffset.Parse(max.Values[0], CultureInfo.InvariantCulture);
    }

    private static bool OutsideTextLength(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        int length = value.Values[0].EnumerateRunes().Count();
        return OutsideIntegerRange(length, parameters);
    }

    private static Result<bool> DoesNotMatchPattern(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters)
    {
        try
        {
            return !Regex.IsMatch(
                value.Values[0],
                parameters["pattern"].Values[0],
                RegexOptions.CultureInvariant,
                PatternTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return Result.Failure<bool>("System text pattern exceeded its execution limit.");
        }
        catch (ArgumentException)
        {
            return Result.Failure<bool>("System text pattern is invalid.");
        }
    }

    private static bool DoesNotMatchFormat(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters) =>
        parameters["format"].Values[0] switch
        {
            "Email" => !MailAddress.TryCreate(value.Values[0], out _),
            "Url" => !Uri.TryCreate(value.Values[0], UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("http" or "https"),
            "Uuid" => !Guid.TryParse(value.Values[0], out _),
            _ => true,
        };

    private static bool OutsideSelectionCount(
        RuleValue value,
        IReadOnlyDictionary<string, RuleValue> parameters) =>
        OutsideIntegerRange(value.Values.Count, parameters);

    private static bool OutsideIntegerRange(
        int value,
        IReadOnlyDictionary<string, RuleValue> parameters) =>
        parameters.TryGetValue("min", out RuleValue? min) &&
            value < int.Parse(min.Values[0], CultureInfo.InvariantCulture) ||
        parameters.TryGetValue("max", out RuleValue? max) &&
            value > int.Parse(max.Values[0], CultureInfo.InvariantCulture);

    private static Result<IReadOnlyDictionary<string, RuleValue>> MapParameters(
        IReadOnlyList<RuleParameterDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        Dictionary<string, RuleValueDto> values = new(StringComparer.Ordinal);
        foreach ((string key, IReadOnlyList<string> rawValues) in parameters)
        {
            RuleParameterDefinition? definition = definitions.SingleOrDefault(candidate =>
                candidate.Key.Equals(key.Trim(), StringComparison.Ordinal));
            if (definition is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameter is not supported.");
            values[key.Trim()] = new RuleValueDto(
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

    private static RuleOutcomeDto SystemOutcome(SystemRuleDefinition definition) =>
        new(
            ContractOutcomeKind.Validation,
            $"{definition.Key.Value}.failed",
            ContractRuleSeverity.Error,
            $"{definition.DisplayName} validation failed.",
            Decision: null);

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
        SystemRuleDefinition? SystemDefinition,
        RuleConditionNode? Condition,
        RuleOutcomeDto Outcome,
        IReadOnlyDictionary<string, RuleValue> Parameters)
    {
        public static ResolvedRule System(
            SystemRuleDefinition definition,
            IReadOnlyDictionary<string, RuleValue> parameters) =>
            new(definition, null, SystemOutcome(definition), parameters);

        public static ResolvedRule Workspace(
            RuleConditionNode condition,
            RuleOutcomeDto outcome,
            IReadOnlyDictionary<string, RuleValue> parameters) =>
            new(null, condition, outcome, parameters);
    }
}
