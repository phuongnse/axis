using System.Globalization;
using System.Text.RegularExpressions;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractRuleScope = Axis.Rules.Contracts.RuleScope;
using Contracts = Axis.Rules.Contracts;
using DomainRuleLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;

namespace Axis.Rules.Application;

public sealed class RuleApplicationValidator(IRuleDefinitionRepository repository)
    : IRuleApplicationValidator
{
    private static readonly TimeSpan PatternValidationTimeout = TimeSpan.FromMilliseconds(250);

    public async Task<RuleApplicationValidationResult> ValidateAsync(
        RuleApplicationValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorkspaceId == Guid.Empty)
            return Invalid("workspace_required", "Workspace scope is required.");

        SystemRuleDefinition? system = SystemRuleCatalog.Find(
            request.DefinitionKey,
            request.DefinitionVersion);
        if (system is not null)
            return ValidateSystem(system, request.Target, request.Parameters);

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(request.DefinitionKey);
        if (key.IsFailure)
            return Invalid("definition_not_found", "Rule definition was not found.");

        RuleDefinition? workspaceDefinition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            request.WorkspaceId,
            cancellationToken);
        if (workspaceDefinition is null || workspaceDefinition.Status == DomainRuleLifecycleStatus.Archived)
            return Invalid("definition_not_found", "Rule definition was not found.");

        RuleDefinitionVersion? version = workspaceDefinition.FindVersion(request.DefinitionVersion);
        if (version is null)
            return Invalid("version_not_found", "Published rule version was not found.");

        if ((ContractRuleScope)version.Scope != request.Target.Scope ||
            !version.ContextKey.Value.Equals(request.Target.ContextKey, StringComparison.Ordinal) ||
            version.ContextSchemaVersion != request.Target.ContextSchemaVersion)
        {
            return Invalid("rule_incompatible", "Rule version is not compatible with the target context.");
        }

        return ValidateParameters(version.Parameters, request.Parameters);
    }

    internal static RuleApplicationValidationResult ValidateSystem(
        SystemRuleDefinition definition,
        RuleApplicationTarget target,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        if ((ContractRuleScope)definition.Scope != target.Scope ||
            !definition.Applicability.TargetTypeKeys.Contains(target.TargetTypeKey, StringComparer.Ordinal))
        {
            return Invalid("rule_incompatible", "Rule is not compatible with the target type.");
        }

        foreach ((string key, IReadOnlyList<string> allowedValues) in
                 definition.Applicability.ConfigurationConstraints)
        {
            if (!target.Configuration.TryGetValue(key, out IReadOnlyList<string>? targetValues) ||
                !targetValues.Any(value => allowedValues.Contains(value, StringComparer.Ordinal)))
            {
                return Invalid("rule_incompatible", "Rule is not compatible with the target configuration.");
            }
        }

        RuleApplicationValidationResult schema = ValidateParameters(definition.Parameters, parameters);
        if (!schema.IsValid)
            return schema;

        IReadOnlyDictionary<string, IReadOnlyList<string>> canonical = schema.CanonicalParameters!;
        return definition.Key.Value switch
        {
            RuleDefinitionKeys.Required => schema,
            RuleDefinitionKeys.NumericRange => ValidateNumericRange(target.TargetTypeKey, canonical),
            RuleDefinitionKeys.DecimalPrecision => ValidateDecimalPrecision(canonical),
            RuleDefinitionKeys.DateRange => ValidateRange<DateOnly>(
                canonical,
                value => DateOnly.ParseExact(value[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                "Date range"),
            RuleDefinitionKeys.DateTimeRange => ValidateRange<DateTimeOffset>(
                canonical,
                value => DateTimeOffset.Parse(value[0], CultureInfo.InvariantCulture),
                "DateTime range"),
            RuleDefinitionKeys.TextLength => ValidateNonNegativeIntegerRange(canonical, "Text length"),
            RuleDefinitionKeys.TextPattern => ValidateTextPattern(canonical),
            RuleDefinitionKeys.TextFormat => schema,
            RuleDefinitionKeys.ChoiceSelectionCount => ValidateNonNegativeIntegerRange(
                canonical,
                "Choice selection count"),
            _ => Invalid("definition_not_found", "Rule definition is not supported."),
        };
    }

    private static RuleApplicationValidationResult ValidateParameters(
        IReadOnlyList<RuleParameterDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        Dictionary<string, RuleParameterDefinition> schema = definitions
            .ToDictionary(parameter => parameter.Key, StringComparer.Ordinal);
        Dictionary<string, RuleValueDto> typedParameters = new(StringComparer.Ordinal);
        foreach ((string rawKey, IReadOnlyList<string> values) in parameters)
        {
            string key = rawKey.Trim();
            if (!schema.TryGetValue(key, out RuleParameterDefinition? parameter))
                return Invalid("parameter_unknown", "Rule parameter is not supported.");

            typedParameters[key] = new RuleValueDto(
                (Contracts.RuleValueType)parameter.Type,
                values);
        }

        Result<IReadOnlyDictionary<string, RuleValue>> result =
            RuleParameterValidator.Validate(definitions, typedParameters);
        return result.IsSuccess
            ? RuleApplicationValidationResult.Valid(
                result.Value.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.Values.ToArray(),
                    StringComparer.Ordinal))
            : Invalid("parameter_invalid", result.Error);
    }

    private static RuleApplicationValidationResult ValidateNumericRange(
        string targetType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        RuleApplicationValidationResult range = ValidateRange<decimal>(
            parameters,
            value => decimal.Parse(value[0], CultureInfo.InvariantCulture),
            "Numeric range");
        if (!range.IsValid || !targetType.Equals("Integer", StringComparison.Ordinal))
            return range;

        foreach (string key in new[] { "min", "max" })
        {
            if (parameters.TryGetValue(key, out IReadOnlyList<string>? value))
            {
                decimal bound = decimal.Parse(value[0], CultureInfo.InvariantCulture);
                if (decimal.Truncate(bound) != bound)
                    return Invalid("range_invalid", "Integer range bounds must be whole numbers.");
            }
        }

        return range;
    }

    private static RuleApplicationValidationResult ValidateDecimalPrecision(
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        int precision = int.Parse(parameters["precision"][0], CultureInfo.InvariantCulture);
        int scale = int.Parse(parameters["scale"][0], CultureInfo.InvariantCulture);
        return precision is < 1 or > 38 || scale < 0 || scale > precision
            ? Invalid("precision_invalid", "Decimal precision must be 1-38 and scale cannot exceed precision.")
            : RuleApplicationValidationResult.Valid(parameters);
    }

    private static RuleApplicationValidationResult ValidateNonNegativeIntegerRange(
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters,
        string label)
    {
        RuleApplicationValidationResult range = ValidateRange<int>(
            parameters,
            value => int.Parse(value[0], CultureInfo.InvariantCulture),
            label);
        if (!range.IsValid)
            return range;

        return parameters.Values.Any(value =>
            int.Parse(value[0], CultureInfo.InvariantCulture) < 0)
            ? Invalid("range_invalid", $"{label} bounds cannot be negative.")
            : range;
    }

    private static RuleApplicationValidationResult ValidateRange<TValue>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters,
        Func<IReadOnlyList<string>, TValue> parse,
        string label)
        where TValue : struct, IComparable<TValue>
    {
        bool hasMin = parameters.TryGetValue("min", out IReadOnlyList<string>? minDto);
        bool hasMax = parameters.TryGetValue("max", out IReadOnlyList<string>? maxDto);
        if (!hasMin && !hasMax)
            return Invalid("range_required", $"{label} requires at least one bound.");

        if (hasMin && hasMax && parse(minDto!).CompareTo(parse(maxDto!)) > 0)
            return Invalid("range_invalid", $"{label} minimum cannot exceed maximum.");

        return RuleApplicationValidationResult.Valid(parameters);
    }

    private static RuleApplicationValidationResult ValidateTextPattern(
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        string pattern = parameters["pattern"][0];
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, PatternValidationTimeout);
            return RuleApplicationValidationResult.Valid(parameters);
        }
        catch (ArgumentException)
        {
            return Invalid("pattern_invalid", "Text pattern is invalid.");
        }
    }

    private static RuleApplicationValidationResult Invalid(string code, string error) =>
        RuleApplicationValidationResult.Invalid(code, error);
}
