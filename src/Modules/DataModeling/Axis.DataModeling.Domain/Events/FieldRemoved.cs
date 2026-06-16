using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record FieldRemoved(
    Guid ModelId,
    Guid workspaceId,
    Guid FieldId,
    string FieldName) : IDomainEvent;
