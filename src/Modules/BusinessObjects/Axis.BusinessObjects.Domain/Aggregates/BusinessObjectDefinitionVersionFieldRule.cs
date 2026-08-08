using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersionFieldRule : Entity<BusinessObjectDefinitionVersionFieldRuleId>
{
    private BusinessObjectDefinitionVersionFieldRule(
        BusinessObjectDefinitionVersionFieldRuleId id,
        BusinessObjectFieldRuleId sourceFieldRuleId,
        Guid bindingId,
        int bindingRevision,
        int order,
        string? bindingKey)
        : base(id)
    {
        SourceFieldRuleId = sourceFieldRuleId;
        BindingId = bindingId;
        BindingRevision = bindingRevision;
        Order = order;
        BindingKey = bindingKey;
    }

    public BusinessObjectFieldRuleId SourceFieldRuleId { get; private set; }
    public Guid BindingId { get; private set; }
    public int BindingRevision { get; private set; }
    public int Order { get; private set; }
    public string? BindingKey { get; private set; }

    public static BusinessObjectDefinitionVersionFieldRule FromCurrentRule(BusinessObjectFieldRule rule) =>
        new(
            BusinessObjectDefinitionVersionFieldRuleId.New(),
            rule.Id,
            rule.BindingId,
            rule.BindingRevision,
            rule.Order,
            rule.BindingKey);
}
