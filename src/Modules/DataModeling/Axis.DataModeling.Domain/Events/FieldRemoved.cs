using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record FieldRemoved(
    Guid ModelId,
    Guid OrganizationId,
    Guid FieldId,
    string FieldName) : IDomainEvent;
