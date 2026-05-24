using Axis.DataModeling.Domain.Enums;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record FieldUpdated(
    Guid ModelId,
    Guid OrganizationId,
    Guid FieldId,
    string FieldName,
    FieldType FieldType,
    string Label,
    bool IsRequired) : IDomainEvent;
