using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed record BusinessObjectFieldRuleSpec(
    Guid BindingId,
    BusinessObjectFieldRuleId? Id = null);
