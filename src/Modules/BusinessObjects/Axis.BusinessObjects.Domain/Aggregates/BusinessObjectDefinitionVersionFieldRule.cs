using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersionFieldRule : Entity<BusinessObjectDefinitionVersionFieldRuleId>
{
    private Dictionary<string, string[]> _parameters = new(StringComparer.Ordinal);

    public BusinessObjectFieldRuleId SourceFieldRuleId { get; private set; }
    public string DefinitionKey { get; private set; }
    public int DefinitionVersion { get; private set; }
    public int Order { get; private set; }
    public IReadOnlyDictionary<string, string[]> Parameters =>
        _parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

    private BusinessObjectDefinitionVersionFieldRule(
        BusinessObjectDefinitionVersionFieldRuleId id,
        BusinessObjectFieldRuleId sourceFieldRuleId,
        string definitionKey,
        int definitionVersion,
        int order)
        : this(
            id,
            sourceFieldRuleId,
            definitionKey,
            definitionVersion,
            order,
            new Dictionary<string, string[]>(StringComparer.Ordinal))
    {
    }

    private BusinessObjectDefinitionVersionFieldRule(
        BusinessObjectDefinitionVersionFieldRuleId id,
        BusinessObjectFieldRuleId sourceFieldRuleId,
        string definitionKey,
        int definitionVersion,
        int order,
        IReadOnlyDictionary<string, string[]> parameters)
        : base(id)
    {
        SourceFieldRuleId = sourceFieldRuleId;
        DefinitionKey = definitionKey;
        DefinitionVersion = definitionVersion;
        Order = order;
        _parameters = parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public static BusinessObjectDefinitionVersionFieldRule FromCurrentRule(BusinessObjectFieldRule rule) =>
        new(
            BusinessObjectDefinitionVersionFieldRuleId.New(),
            rule.Id,
            rule.DefinitionKey,
            rule.DefinitionVersion,
            rule.Order,
            rule.Parameters);
}
