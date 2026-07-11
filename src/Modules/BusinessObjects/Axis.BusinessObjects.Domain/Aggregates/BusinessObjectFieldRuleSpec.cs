using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed record BusinessObjectFieldRuleSpec(
    string DefinitionKey,
    int DefinitionVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Parameters = null,
    BusinessObjectFieldRuleId? Id = null);
