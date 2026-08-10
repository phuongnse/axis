using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectFieldRule : Entity<BusinessObjectFieldRuleId>
{
    private BusinessObjectFieldRule(
        BusinessObjectFieldRuleId id,
        Guid bindingId,
        int bindingRevision,
        int order,
        string? bindingKey)
        : base(id)
    {
        BindingId = bindingId;
        BindingRevision = bindingRevision;
        Order = order;
        BindingKey = bindingKey;
    }

    public Guid BindingId { get; private set; }
    public int BindingRevision { get; private set; }
    public int Order { get; private set; }
    public string? BindingKey { get; private set; }

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
            if (spec.BindingId == Guid.Empty || spec.BindingRevision <= 0 || !seenBindingIds.Add(spec.BindingId) ||
                (spec.BindingKey is not null &&
                    (spec.BindingKey.Length is < 1 or > 200 || spec.BindingKey != spec.BindingKey.Trim())))
                return Result.Failure<IReadOnlyList<BusinessObjectFieldRule>>(
                    "Field rule bindings must be unique and non-empty.");
            rules.Add(new BusinessObjectFieldRule(
                spec.Id ?? BusinessObjectFieldRuleId.New(),
                spec.BindingId,
                spec.BindingRevision,
                index,
                spec.BindingKey));
        }
        return rules;
    }

    internal void Apply(BusinessObjectFieldRule source)
    {
        BindingId = source.BindingId;
        BindingRevision = source.BindingRevision;
        Order = source.Order;
        BindingKey = source.BindingKey;
    }
}
