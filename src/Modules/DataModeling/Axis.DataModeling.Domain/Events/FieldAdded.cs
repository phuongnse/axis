using Axis.DataModeling.Domain.Enums;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record FieldAdded(
    Guid ModelId,
    Guid tenantId,
    Guid FieldId,
    string FieldName,
    FieldType FieldType,
    string Label,
    bool IsRequired,
    int DisplayOrder) : IDomainEvent;
