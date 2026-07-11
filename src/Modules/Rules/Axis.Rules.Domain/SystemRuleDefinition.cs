using System.Collections.ObjectModel;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed record RuleApplicability
{
    private RuleApplicability(
        IReadOnlyList<string> targetTypeKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> configurationConstraints)
    {
        TargetTypeKeys = targetTypeKeys;
        ConfigurationConstraints = configurationConstraints;
    }

    public IReadOnlyList<string> TargetTypeKeys { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ConfigurationConstraints { get; }

    public static Result<RuleApplicability> Create(
        IReadOnlyList<string> targetTypeKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? configurationConstraints = null)
    {
        if (targetTypeKeys is null)
            return Result.Failure<RuleApplicability>("Rule applicability requires at least one target type.");

        string[] normalizedTypes = targetTypeKeys
            .Select(type => type?.Trim() ?? string.Empty)
            .Where(type => type.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedTypes.Length == 0)
            return Result.Failure<RuleApplicability>("Rule applicability requires at least one target type.");

        Dictionary<string, IReadOnlyList<string>> normalizedConstraints = new(StringComparer.Ordinal);
        foreach ((string key, IReadOnlyList<string> values) in configurationConstraints
                     ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal))
        {
            if (key is null || values is null || values.Any(value => value is null))
                return Result.Failure<RuleApplicability>("Rule applicability configuration constraints are invalid.");

            string normalizedKey = key?.Trim() ?? string.Empty;
            string[] normalizedValues = values
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (normalizedKey.Length == 0 || normalizedValues.Length == 0)
                return Result.Failure<RuleApplicability>("Rule applicability configuration constraints are invalid.");

            if (!normalizedConstraints.TryAdd(normalizedKey, Array.AsReadOnly(normalizedValues)))
                return Result.Failure<RuleApplicability>("Rule applicability configuration keys must be unique.");
        }

        return new RuleApplicability(
            Array.AsReadOnly(normalizedTypes),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(normalizedConstraints));
    }
}

public sealed record SystemRuleDefinition
{
    private SystemRuleDefinition(
        RuleDefinitionKey key,
        int version,
        string displayName,
        string description,
        RuleScope scope,
        RuleOutcomeKind outcomeKind,
        RuleApplicability applicability,
        IReadOnlyList<RuleParameterDefinition> parameters)
    {
        Key = key;
        Version = version;
        DisplayName = displayName;
        Description = description;
        Scope = scope;
        OutcomeKind = outcomeKind;
        Applicability = applicability;
        Parameters = parameters;
    }

    public RuleDefinitionKey Key { get; }
    public int Version { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RuleOrigin Origin => RuleOrigin.System;
    public RuleScope Scope { get; }
    public RuleOutcomeKind OutcomeKind { get; }
    public RuleLifecycleStatus Status => RuleLifecycleStatus.Published;
    public RuleApplicability Applicability { get; }
    public IReadOnlyList<RuleParameterDefinition> Parameters { get; }

    public static Result<SystemRuleDefinition> Create(
        string key,
        int version,
        string displayName,
        string description,
        RuleScope scope,
        RuleOutcomeKind outcomeKind,
        RuleApplicability applicability,
        IReadOnlyList<RuleParameterDefinition> parameters)
    {
        Result<RuleDefinitionKey> definitionKey = RuleDefinitionKey.Create(key);
        if (definitionKey.IsFailure)
            return Result.Failure<SystemRuleDefinition>(definitionKey.Error);

        if (version <= 0)
            return Result.Failure<SystemRuleDefinition>("System rule version must be positive.");

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<SystemRuleDefinition>("System rule display name is required.");

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<SystemRuleDefinition>("System rule description is required.");

        if (!Enum.IsDefined(scope) || !Enum.IsDefined(outcomeKind))
            return Result.Failure<SystemRuleDefinition>("System rule scope or outcome is not supported.");

        if (applicability is null)
            return Result.Failure<SystemRuleDefinition>("System rule applicability is required.");

        if (parameters is null || parameters.Any(parameter => parameter is null))
            return Result.Failure<SystemRuleDefinition>("System rule parameters are required.");

        if (parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count() != parameters.Count)
            return Result.Failure<SystemRuleDefinition>("System rule parameter keys must be unique.");

        return new SystemRuleDefinition(
            definitionKey.Value,
            version,
            displayName.Trim(),
            description.Trim(),
            scope,
            outcomeKind,
            applicability,
            Array.AsReadOnly(parameters.ToArray()));
    }
}
