using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinitionVersionFieldRule : Entity<ObjectFieldRuleId>
{
    private Dictionary<string, string[]> _parameters = new(StringComparer.Ordinal);

    public string DefinitionKey { get; private set; }
    public int Order { get; private set; }
    public IReadOnlyDictionary<string, string[]> Parameters =>
        _parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

    private ObjectDefinitionVersionFieldRule(ObjectFieldRuleId id, string definitionKey, int order)
        : this(id, definitionKey, order, new Dictionary<string, string[]>(StringComparer.Ordinal))
    {
    }

    private ObjectDefinitionVersionFieldRule(
        ObjectFieldRuleId id,
        string definitionKey,
        int order,
        IReadOnlyDictionary<string, string[]> parameters)
        : base(id)
    {
        DefinitionKey = definitionKey;
        Order = order;
        _parameters = parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public static ObjectDefinitionVersionFieldRule FromCurrentRule(ObjectFieldRule rule) =>
        new(
            ObjectFieldRuleId.New(),
            rule.DefinitionKey,
            rule.Order,
            rule.Parameters);
}
