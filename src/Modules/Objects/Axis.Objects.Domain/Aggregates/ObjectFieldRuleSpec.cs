namespace Axis.Objects.Domain.Aggregates;

public sealed record ObjectFieldRuleSpec(
    string DefinitionKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Parameters = null);
