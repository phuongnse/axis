using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed record BusinessObjectChoiceOptionSpec(
    string OptionKey,
    string Label,
    int Order,
    BusinessObjectChoiceOptionId? Id = null);

public sealed record BusinessObjectChoiceFieldConfigurationSpec(
    BusinessObjectChoiceSelectionMode SelectionMode,
    IReadOnlyList<BusinessObjectChoiceOptionSpec> Options);
