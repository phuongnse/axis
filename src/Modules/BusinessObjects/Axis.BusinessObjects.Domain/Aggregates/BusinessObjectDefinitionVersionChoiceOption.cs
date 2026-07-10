using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersionChoiceOption : Entity<BusinessObjectDefinitionVersionChoiceOptionId>
{
    public BusinessObjectChoiceOptionId SourceChoiceOptionId { get; private set; }
    public BusinessObjectChoiceOptionKey Key { get; private set; }
    public string Label { get; private set; }
    public int Order { get; private set; }

    private BusinessObjectDefinitionVersionChoiceOption(
        BusinessObjectDefinitionVersionChoiceOptionId id,
        BusinessObjectChoiceOptionId sourceChoiceOptionId,
        BusinessObjectChoiceOptionKey key,
        string label,
        int order)
        : base(id)
    {
        SourceChoiceOptionId = sourceChoiceOptionId;
        Key = key;
        Label = label;
        Order = order;
    }

    public static BusinessObjectDefinitionVersionChoiceOption FromCurrentOption(BusinessObjectChoiceOption option) =>
        new(
            BusinessObjectDefinitionVersionChoiceOptionId.New(),
            option.Id,
            option.Key,
            option.Label,
            option.Order);
}
