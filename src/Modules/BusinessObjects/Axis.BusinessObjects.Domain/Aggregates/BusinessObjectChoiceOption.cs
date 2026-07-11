using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectChoiceOption : Entity<BusinessObjectChoiceOptionId>
{
    public BusinessObjectChoiceOptionKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }

    private BusinessObjectChoiceOption(
        BusinessObjectChoiceOptionId id,
        BusinessObjectChoiceOptionKey key,
        string label,
        int order)
        : base(id)
    {
        Key = key;
        Label = label;
        Order = order;
    }

    public static Result<BusinessObjectChoiceOption> Create(
        BusinessObjectChoiceOptionId id,
        BusinessObjectChoiceOptionSpec spec)
    {
        Result<BusinessObjectChoiceOptionKey> key = BusinessObjectChoiceOptionKey.Create(spec.OptionKey);
        if (key.IsFailure)
            return Result.Failure<BusinessObjectChoiceOption>(key.Error);

        if (string.IsNullOrWhiteSpace(spec.Label) || spec.Label.Trim().Length > 200)
            return Result.Failure<BusinessObjectChoiceOption>("Choice option label is required and cannot exceed 200 characters.");

        if (spec.Order < 0)
            return Result.Failure<BusinessObjectChoiceOption>("Choice option order cannot be negative.");

        return new BusinessObjectChoiceOption(id, key.Value, spec.Label.Trim(), spec.Order);
    }

    internal void Apply(BusinessObjectChoiceOption source)
    {
        Label = source.Label;
        Order = source.Order;
    }
}
