using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersionFieldRule : Entity<BusinessObjectDefinitionVersionFieldRuleId>
{
    private BusinessObjectDefinitionVersionFieldRule(
        BusinessObjectDefinitionVersionFieldRuleId id,
        BusinessObjectFieldRuleId sourceFieldRuleId,
        Guid bindingId,
        int order)
        : base(id)
    {
        SourceFieldRuleId = sourceFieldRuleId;
        BindingId = bindingId;
        Order = order;
    }

    public BusinessObjectFieldRuleId SourceFieldRuleId { get; private set; }
    public Guid BindingId { get; private set; }
    public int Order { get; private set; }

    public static BusinessObjectDefinitionVersionFieldRule FromCurrentRule(BusinessObjectFieldRule rule) =>
        new(
            BusinessObjectDefinitionVersionFieldRuleId.New(),
            rule.Id,
            rule.BindingId,
            rule.Order);
}
