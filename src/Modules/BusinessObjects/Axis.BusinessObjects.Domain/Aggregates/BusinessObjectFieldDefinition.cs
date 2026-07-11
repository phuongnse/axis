using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectFieldDefinition : Entity<BusinessObjectFieldDefinitionId>
{
    private readonly List<BusinessObjectFieldRule> _rules = [];
    private readonly List<BusinessObjectChoiceOption> _choiceOptions = [];

    public BusinessObjectFieldKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }
    public BusinessObjectFieldType FieldType { get; private set; }
    public BusinessObjectChoiceSelectionMode? ChoiceSelectionMode { get; private set; }
    public IReadOnlyList<BusinessObjectChoiceOption> ChoiceOptions =>
        _choiceOptions.OrderBy(option => option.Order).ToArray();
    public IReadOnlyList<BusinessObjectFieldRule> Rules =>
        _rules.OrderBy(rule => rule.Order).ToArray();

    private BusinessObjectFieldDefinition(
        BusinessObjectFieldDefinitionId id,
        BusinessObjectFieldKey key,
        string label,
        int order,
        BusinessObjectFieldType fieldType)
        : this(id, key, label, order, fieldType, null, [], [])
    {
    }

    private BusinessObjectFieldDefinition(
        BusinessObjectFieldDefinitionId id,
        BusinessObjectFieldKey key,
        string label,
        int order,
        BusinessObjectFieldType fieldType,
        BusinessObjectChoiceSelectionMode? choiceSelectionMode,
        IReadOnlyList<BusinessObjectChoiceOption> choiceOptions,
        IReadOnlyList<BusinessObjectFieldRule> rules)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
        FieldType = fieldType;
        ChoiceSelectionMode = choiceSelectionMode;
        _choiceOptions.AddRange(choiceOptions.OrderBy(option => option.Order));
        _rules.AddRange(rules.OrderBy(rule => rule.Order));
    }

    public static Result<BusinessObjectFieldDefinition> Create(
        BusinessObjectFieldDefinitionId id,
        BusinessObjectFieldDefinitionSpec spec)
    {
        Result<BusinessObjectFieldKey> key = BusinessObjectFieldKey.Create(spec.FieldKey);
        if (key.IsFailure)
            return Result.Failure<BusinessObjectFieldDefinition>(key.Error);

        if (string.IsNullOrWhiteSpace(spec.Label))
            return Result.Failure<BusinessObjectFieldDefinition>("Field label is required.");

        if (!Enum.IsDefined(spec.FieldType))
            return Result.Failure<BusinessObjectFieldDefinition>("Field type is not supported.");

        Result<IReadOnlyList<BusinessObjectChoiceOption>> options = CreateChoiceOptions(
            spec.FieldType,
            spec.ChoiceConfiguration);
        if (options.IsFailure)
            return Result.Failure<BusinessObjectFieldDefinition>(options.Error);

        Result<IReadOnlyList<BusinessObjectFieldRule>> rules = BusinessObjectFieldRule.CreateMany(spec.Rules);
        if (rules.IsFailure)
            return Result.Failure<BusinessObjectFieldDefinition>(rules.Error);

        return new BusinessObjectFieldDefinition(
            id,
            key.Value,
            spec.Label.Trim(),
            spec.Order,
            spec.FieldType,
            spec.ChoiceConfiguration?.SelectionMode,
            options.Value,
            rules.Value);
    }

    internal void Apply(BusinessObjectFieldDefinition source)
    {
        Label = source.Label;
        Order = source.Order;
        FieldType = source.FieldType;
        ChoiceSelectionMode = source.ChoiceSelectionMode;
        ReplaceChoiceOptions(source.ChoiceOptions);
        ReplaceRules(source.Rules);
    }

    internal Result ValidateChildIdentities(BusinessObjectFieldDefinitionSpec spec)
    {
        if (spec.ChoiceConfiguration is not null)
        {
            Dictionary<BusinessObjectChoiceOptionId, BusinessObjectChoiceOption> optionsById = _choiceOptions
                .ToDictionary(option => option.Id);
            Dictionary<string, BusinessObjectChoiceOption> optionsByKey = _choiceOptions
                .ToDictionary(option => option.Key.Value, StringComparer.Ordinal);
            HashSet<BusinessObjectChoiceOptionId> seenOptionIds = [];

            foreach (BusinessObjectChoiceOptionSpec option in spec.ChoiceConfiguration.Options)
            {
                string key = option.OptionKey?.Trim() ?? string.Empty;
                if (option.Id is not BusinessObjectChoiceOptionId optionId)
                {
                    if (optionsByKey.ContainsKey(key))
                        return Result.Failure("Existing choice option identity is required when saving a field.");
                    continue;
                }

                if (!seenOptionIds.Add(optionId))
                    return Result.Failure("Choice option identities must be unique.");
                if (!optionsById.TryGetValue(optionId, out BusinessObjectChoiceOption? existingOption))
                    return Result.Failure("Choice option identity does not belong to this field.");
                if (!StringComparer.Ordinal.Equals(existingOption.Key.Value, key))
                    return Result.Failure("Persisted choice option keys cannot be changed.");
            }
        }

        Dictionary<BusinessObjectFieldRuleId, BusinessObjectFieldRule> rulesById = _rules
            .ToDictionary(rule => rule.Id);
        Dictionary<string, BusinessObjectFieldRule> rulesByKey = _rules
            .ToDictionary(rule => rule.DefinitionKey, StringComparer.Ordinal);
        HashSet<BusinessObjectFieldRuleId> seenRuleIds = [];
        foreach (BusinessObjectFieldRuleSpec rule in spec.Rules ?? [])
        {
            string definitionKey = rule.DefinitionKey?.Trim() ?? string.Empty;
            if (rule.Id is not BusinessObjectFieldRuleId ruleId)
            {
                if (rulesByKey.ContainsKey(definitionKey))
                    return Result.Failure("Existing field rule identity is required when saving a field.");
                continue;
            }

            if (!seenRuleIds.Add(ruleId))
                return Result.Failure("Field rule identities must be unique.");
            if (!rulesById.TryGetValue(ruleId, out BusinessObjectFieldRule? existingRule))
                return Result.Failure("Field rule identity does not belong to this field.");
            if (!StringComparer.Ordinal.Equals(existingRule.DefinitionKey, definitionKey))
                return Result.Failure("Persisted field rule definition keys cannot be changed.");
        }

        return Result.Success();
    }

    private static Result<IReadOnlyList<BusinessObjectChoiceOption>> CreateChoiceOptions(
        BusinessObjectFieldType fieldType,
        BusinessObjectChoiceFieldConfigurationSpec? configuration)
    {
        if (fieldType != BusinessObjectFieldType.Choice)
        {
            return configuration is null
                ? Array.Empty<BusinessObjectChoiceOption>()
                : Result.Failure<IReadOnlyList<BusinessObjectChoiceOption>>(
                    "Choice configuration is only valid for Choice fields.");
        }

        if (configuration is null || !Enum.IsDefined(configuration.SelectionMode))
            return Result.Failure<IReadOnlyList<BusinessObjectChoiceOption>>("Choice selection mode is required.");

        if (configuration.Options.Count == 0)
            return Result.Failure<IReadOnlyList<BusinessObjectChoiceOption>>("Choice fields require at least one option.");

        HashSet<string> keys = new(StringComparer.Ordinal);
        HashSet<int> orders = [];
        List<BusinessObjectChoiceOption> options = [];
        foreach (BusinessObjectChoiceOptionSpec optionSpec in configuration.Options.OrderBy(option => option.Order))
        {
            if (!keys.Add(optionSpec.OptionKey.Trim()))
                return Result.Failure<IReadOnlyList<BusinessObjectChoiceOption>>("Choice option keys must be unique.");
            if (!orders.Add(optionSpec.Order))
                return Result.Failure<IReadOnlyList<BusinessObjectChoiceOption>>("Choice option ordering must be unique.");

            Result<BusinessObjectChoiceOption> option = BusinessObjectChoiceOption.Create(
                optionSpec.Id ?? BusinessObjectChoiceOptionId.New(),
                optionSpec);
            if (option.IsFailure)
                return Result.Failure<IReadOnlyList<BusinessObjectChoiceOption>>(option.Error);
            options.Add(option.Value);
        }

        return options;
    }

    private void ReplaceChoiceOptions(IReadOnlyList<BusinessObjectChoiceOption> plannedOptions)
    {
        Dictionary<BusinessObjectChoiceOptionId, BusinessObjectChoiceOption> existingById = _choiceOptions
            .ToDictionary(option => option.Id);
        List<BusinessObjectChoiceOption> next = [];
        foreach (BusinessObjectChoiceOption planned in plannedOptions.OrderBy(option => option.Order))
        {
            if (existingById.TryGetValue(planned.Id, out BusinessObjectChoiceOption? existing))
            {
                existing.Apply(planned);
                next.Add(existing);
            }
            else
            {
                next.Add(planned);
            }
        }

        _choiceOptions.Clear();
        _choiceOptions.AddRange(next);
    }

    private void ReplaceRules(IReadOnlyList<BusinessObjectFieldRule> plannedRules)
    {
        Dictionary<BusinessObjectFieldRuleId, BusinessObjectFieldRule> existingById = _rules
            .ToDictionary(rule => rule.Id);
        List<BusinessObjectFieldRule> next = [];
        foreach (BusinessObjectFieldRule planned in plannedRules.OrderBy(rule => rule.Order))
        {
            if (existingById.TryGetValue(planned.Id, out BusinessObjectFieldRule? existing))
            {
                existing.Apply(planned);
                next.Add(existing);
            }
            else
            {
                next.Add(planned);
            }
        }

        _rules.Clear();
        _rules.AddRange(next);
    }
}
