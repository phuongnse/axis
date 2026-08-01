using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectFieldRule : Entity<BusinessObjectFieldRuleId>
{
    private BusinessObjectFieldRule(
        BusinessObjectFieldRuleId id,
        Guid bindingId,
        int order)
        : base(id)
    {
        BindingId = bindingId;
        Order = order;
    }

    public Guid BindingId { get; private set; }
    public int Order { get; private set; }

    public static Result<IReadOnlyList<BusinessObjectFieldRule>> CreateMany(
        IReadOnlyList<BusinessObjectFieldRuleSpec>? specs)
    {
        if (specs is null || specs.Count == 0)
            return Array.Empty<BusinessObjectFieldRule>();

        HashSet<Guid> seenBindingIds = [];
        List<BusinessObjectFieldRule> rules = [];
        for (int index = 0; index < specs.Count; index++)
        {
            BusinessObjectFieldRuleSpec spec = specs[index];
            if (spec.BindingId == Guid.Empty || !seenBindingIds.Add(spec.BindingId))
                return Result.Failure<IReadOnlyList<BusinessObjectFieldRule>>(
                    "Field rule bindings must be unique and non-empty.");
            rules.Add(new BusinessObjectFieldRule(
                spec.Id ?? BusinessObjectFieldRuleId.New(),
                spec.BindingId,
                index));
        }
        return rules;
    }

    internal void Apply(BusinessObjectFieldRule source)
    {
        BindingId = source.BindingId;
        Order = source.Order;
    }
}
